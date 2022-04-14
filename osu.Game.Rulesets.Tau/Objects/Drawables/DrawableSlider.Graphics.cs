﻿using osu.Framework.Graphics.Textures;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp;
using System;
using System.Collections.Generic;
using osuTK.Graphics;
using osu.Framework.Graphics.Lines;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Primitives;
using osuTK.Graphics.ES30;
using osuTK;
using osu.Framework.Graphics.Batches;
using osu.Framework.Graphics.OpenGL.Vertices;
using osu.Framework.Graphics.Colour;
using osu.Framework.Graphics.Shaders;
using RectangleF = osu.Framework.Graphics.Primitives.RectangleF;
using osu.Framework.Allocation;
using osu.Framework.Caching;
using osu.Framework.Extensions.EnumExtensions;

namespace osu.Game.Rulesets.Tau.Objects.Drawables;

public partial class DrawableSlider {
    public static Texture GenerateSmoothPathTexture ( float radius, Func<float, Color4> colourAt ) {
        const float aa_portion = 0.02f;
        int textureWidth = (int)radius * 2;

        var raw = new Image<Rgba32>( textureWidth, 1 );

        for ( int i = 0; i < textureWidth; i++ ) {
            float progress = (float)i / ( textureWidth - 1 );

            var colour = colourAt( progress );
            raw[i, 0] = new Rgba32( colour.R, colour.G, colour.B, colour.A * Math.Min( progress / aa_portion, 1 ) );
        }

        var texture = new Texture( textureWidth, 1, true );
        texture.SetData( new TextureUpload( raw ) );
        return texture;
    }

    public class SliderPath : Drawable {
        public SliderPath () {
            AutoSizeAxes = Axes.Both;
        }

        [BackgroundDependencyLoader]
        private void load ( ShaderManager shaders ) {
            RoundedTextureShader = shaders.Load( VertexShaderDescriptor.TEXTURE_2, FragmentShaderDescriptor.TEXTURE_ROUNDED );
        }
        public IShader RoundedTextureShader { get; private set; }

        private readonly List<Vector2> vertices = new();
        public IReadOnlyList<Vector2> Vertices {
            get => vertices;
            set {
                vertices.Clear();
                vertices.AddRange( value );

                vertexBoundsCache.Invalidate();
                segmentsCache.Invalidate();

                Invalidate( Invalidation.DrawSize );
            }
        }

        private float pathRadius = 10f;

        /// <summary>
        /// How wide this path is on each side of the line.
        /// </summary>
        /// <remarks>
        /// The actual width of the path is twice the PathRadius.
        /// </remarks>
        public virtual float PathRadius {
            get => pathRadius;
            set {
                if ( pathRadius == value ) return;

                pathRadius = value;

                vertexBoundsCache.Invalidate();
                segmentsCache.Invalidate();

                Invalidate( Invalidation.DrawSize );
            }
        }

        public override Axes RelativeSizeAxes {
            get => base.RelativeSizeAxes;
            set {
                if ( ( AutoSizeAxes & value ) != 0 )
                    throw new InvalidOperationException( "No axis can be relatively sized and automatically sized at the same time." );

                base.RelativeSizeAxes = value;
            }
        }

        private Axes autoSizeAxes;

        /// <summary>
        /// Controls which <see cref="Axes"/> are automatically sized w.r.t. the bounds of the vertices.
        /// It is not allowed to manually set <see cref="Size"/> (or <see cref="Width"/> / <see cref="Height"/>)
        /// on any <see cref="Axes"/> which are automatically sized.
        /// </summary>
        public virtual Axes AutoSizeAxes {
            get => autoSizeAxes;
            set {
                if ( value == autoSizeAxes )
                    return;

                if ( ( RelativeSizeAxes & value ) != 0 )
                    throw new InvalidOperationException( "No axis can be relatively sized and automatically sized at the same time." );

                autoSizeAxes = value;
                OnSizingChanged();
            }
        }

        public override float Width {
            get {
                if ( AutoSizeAxes.HasFlagFast( Axes.X ) )
                    return base.Width = vertexBounds.Width;

                return base.Width;
            }
            set {
                if ( ( AutoSizeAxes & Axes.X ) != 0 )
                    throw new InvalidOperationException( $"The width of a {nameof( Path )} with {nameof( AutoSizeAxes )} can not be set manually." );

                base.Width = value;
            }
        }

        public override float Height {
            get {
                if ( AutoSizeAxes.HasFlagFast( Axes.Y ) )
                    return base.Height = vertexBounds.Height;

                return base.Height;
            }
            set {
                if ( ( AutoSizeAxes & Axes.Y ) != 0 )
                    throw new InvalidOperationException( $"The height of a {nameof( Path )} with {nameof( AutoSizeAxes )} can not be set manually." );

                base.Height = value;
            }
        }

        public override Vector2 Size {
            get {
                if ( AutoSizeAxes != Axes.None )
                    return base.Size = vertexBounds.Size;

                return base.Size;
            }
            set {
                if ( ( AutoSizeAxes & Axes.Both ) != 0 )
                    throw new InvalidOperationException( $"The Size of a {nameof( Path )} with {nameof( AutoSizeAxes )} can not be set manually." );

                base.Size = value;
            }
        }

        private readonly Cached<RectangleF> vertexBoundsCache = new Cached<RectangleF>();

        private RectangleF vertexBounds {
            get {
                if ( vertexBoundsCache.IsValid )
                    return vertexBoundsCache.Value;

                if ( vertices.Count > 0 ) {
                    float minX = 0;
                    float minY = 0;
                    float maxX = 0;
                    float maxY = 0;

                    foreach ( var v in vertices ) {
                        minX = Math.Min( minX, v.X - PathRadius );
                        minY = Math.Min( minY, v.Y - PathRadius );
                        maxX = Math.Max( maxX, v.X + PathRadius );
                        maxY = Math.Max( maxY, v.Y + PathRadius );
                    }

                    return vertexBoundsCache.Value = new RectangleF( minX, minY, maxX - minX, maxY - minY );
                }

                return vertexBoundsCache.Value = new RectangleF( 0, 0, 0, 0 );
            }
        }

        public override bool ReceivePositionalInputAt ( Vector2 screenSpacePos ) {
            var localPos = ToLocalSpace( screenSpacePos );
            float pathRadiusSquared = PathRadius * PathRadius;

            foreach ( var t in segments ) {
                if ( t.DistanceSquaredToPoint( localPos ) <= pathRadiusSquared )
                    return true;
            }

            return false;
        }

        public Vector2 PositionInBoundingBox ( Vector2 pos ) => pos - vertexBounds.TopLeft;

        public void ClearVertices () {
            if ( vertices.Count == 0 )
                return;

            vertices.Clear();

            vertexBoundsCache.Invalidate();
            segmentsCache.Invalidate();

            Invalidate( Invalidation.DrawSize );
        }

        public void AddVertex ( Vector2 pos ) {
            vertices.Add( pos );

            vertexBoundsCache.Invalidate();
            segmentsCache.Invalidate();

            Invalidate( Invalidation.DrawSize );
        }

        private readonly List<Line> segmentsBacking = new List<Line>();
        private readonly Cached segmentsCache = new Cached();
        private List<Line> segments => segmentsCache.IsValid ? segmentsBacking : generateSegments();

        private List<Line> generateSegments () {
            segmentsBacking.Clear();

            if ( vertices.Count > 1 ) {
                Vector2 offset = vertexBounds.TopLeft;
                for ( int i = 0; i < vertices.Count - 1; ++i )
                    segmentsBacking.Add( new Line( vertices[i] - offset, vertices[i + 1] - offset ) );
            }

            segmentsCache.Validate();
            return segmentsBacking;
        }

        private Texture texture;

        public Texture Texture {
            get => texture ?? Texture.WhitePixel;
            set {
                if ( texture == value )
                    return;

                texture = value;
                Invalidate( Invalidation.DrawNode );
            }
        }

        protected override DrawNode CreateDrawNode () => new SliderPathDrawNode( this );

        public class SliderPathDrawNode : DrawNode {
            public const int MAX_RES = 24;

            protected new SliderPath Source => (SliderPath)base.Source;

            private readonly List<Line> segments = new();

            private Texture texture;
            private Vector2 drawSize;
            private float radius;
            private IShader shader;

            // We multiply the size param by 3 such that the amount of vertices is a multiple of the amount of vertices
            // per primitive (triangles in this case). Otherwise overflowing the batch will result in wrong
            // grouping of vertices into primitives.
            private readonly LinearBatch<TexturedVertex2D> halfCircleBatch = new LinearBatch<TexturedVertex2D>( MAX_RES * 100 * 3, 10, PrimitiveType.Triangles );
            private readonly QuadBatch<TexturedVertex2D> quadBatch = new QuadBatch<TexturedVertex2D>( 200, 10 );

            public SliderPathDrawNode ( SliderPath source )
                : base( source ) {
            }

            public override void ApplyState () {
                base.ApplyState();

                segments.Clear();
                segments.AddRange( Source.segments );

                texture = Source.Texture;
                drawSize = Source.DrawSize;
                radius = Source.PathRadius;
                shader = Source.RoundedTextureShader;
            }

            private Vector2 pointOnCircle ( float angle ) => new Vector2( MathF.Sin( angle ), -MathF.Cos( angle ) );

            private Vector2 relativePosition ( Vector2 localPos ) => Vector2.Divide( localPos, drawSize );

            private Color4 colourAt ( Vector2 localPos ) => DrawColourInfo.Colour.HasSingleColour
                ? ( (SRGBColour)DrawColourInfo.Colour ).Linear
                : DrawColourInfo.Colour.Interpolate( relativePosition( localPos ) ).Linear;

            private void addLineCap ( Vector2 origin, float theta, float thetaDiff, RectangleF texRect ) {
                const float step = MathF.PI / MAX_RES;

                float dir = Math.Sign( thetaDiff );
                thetaDiff = dir * thetaDiff;

                if ( thetaDiff > MathF.PI ) {
                    thetaDiff = 2 * MathF.PI - thetaDiff;
                    dir = -dir;
                }

                int amountPoints = (int)Math.Ceiling( thetaDiff / step );

                if ( dir < 0 )
                    theta += MathF.PI;

                Vector2 current = origin + pointOnCircle( theta ) * radius;
                Color4 currentColour = colourAt( current );
                current = Vector2Extensions.Transform( current, DrawInfo.Matrix );

                Vector2 screenOrigin = Vector2Extensions.Transform( origin, DrawInfo.Matrix );
                Color4 originColour = colourAt( origin );

                for ( int i = 1; i <= amountPoints; i++ ) {
                    // Center point
                    halfCircleBatch.Add( new TexturedVertex2D {
                        Position = new Vector2( screenOrigin.X, screenOrigin.Y ),
                        TexturePosition = new Vector2( texRect.Right, texRect.Centre.Y ),
                        Colour = originColour
                    } );

                    // First outer point
                    halfCircleBatch.Add( new TexturedVertex2D {
                        Position = new Vector2( current.X, current.Y ),
                        TexturePosition = new Vector2( texRect.Left, texRect.Centre.Y ),
                        Colour = currentColour
                    } );

                    float angularOffset = Math.Min( i * step, thetaDiff );
                    current = origin + pointOnCircle( theta + dir * angularOffset ) * radius;
                    currentColour = colourAt( current );
                    current = Vector2Extensions.Transform( current, DrawInfo.Matrix );

                    // Second outer point
                    halfCircleBatch.Add( new TexturedVertex2D {
                        Position = new Vector2( current.X, current.Y ),
                        TexturePosition = new Vector2( texRect.Left, texRect.Centre.Y ),
                        Colour = currentColour
                    } );
                }
            }

            private void addLineQuads ( Line line, RectangleF texRect ) {
                Vector2 ortho = line.OrthogonalDirection;
                Line lineLeft = new Line( line.StartPoint + ortho * radius, line.EndPoint + ortho * radius );
                Line lineRight = new Line( line.StartPoint - ortho * radius, line.EndPoint - ortho * radius );

                Line screenLineLeft = new Line( Vector2Extensions.Transform( lineLeft.StartPoint, DrawInfo.Matrix ), Vector2Extensions.Transform( lineLeft.EndPoint, DrawInfo.Matrix ) );
                Line screenLineRight = new Line( Vector2Extensions.Transform( lineRight.StartPoint, DrawInfo.Matrix ), Vector2Extensions.Transform( lineRight.EndPoint, DrawInfo.Matrix ) );
                Line screenLine = new Line( Vector2Extensions.Transform( line.StartPoint, DrawInfo.Matrix ), Vector2Extensions.Transform( line.EndPoint, DrawInfo.Matrix ) );

                quadBatch.Add( new TexturedVertex2D {
                    Position = new Vector2( screenLineRight.EndPoint.X, screenLineRight.EndPoint.Y ),
                    TexturePosition = new Vector2( texRect.Left, texRect.Centre.Y ),
                    Colour = colourAt( lineRight.EndPoint )
                } );
                quadBatch.Add( new TexturedVertex2D {
                    Position = new Vector2( screenLineRight.StartPoint.X, screenLineRight.StartPoint.Y ),
                    TexturePosition = new Vector2( texRect.Left, texRect.Centre.Y ),
                    Colour = colourAt( lineRight.StartPoint )
                } );

                // Each "quad" of the slider is actually rendered as 2 quads, being split in half along the approximating line.
                // On this line the depth is 1 instead of 0, which is done properly handle self-overlap using the depth buffer.
                // Thus the middle vertices need to be added twice (once for each quad).
                Vector2 firstMiddlePoint = new Vector2( screenLine.StartPoint.X, screenLine.StartPoint.Y );
                Vector2 secondMiddlePoint = new Vector2( screenLine.EndPoint.X, screenLine.EndPoint.Y );
                Color4 firstMiddleColour = colourAt( line.StartPoint );
                Color4 secondMiddleColour = colourAt( line.EndPoint );

                for ( int i = 0; i < 2; ++i ) {
                    quadBatch.Add( new TexturedVertex2D {
                        Position = firstMiddlePoint,
                        TexturePosition = new Vector2( texRect.Right, texRect.Centre.Y ),
                        Colour = firstMiddleColour
                    } );
                    quadBatch.Add( new TexturedVertex2D {
                        Position = secondMiddlePoint,
                        TexturePosition = new Vector2( texRect.Right, texRect.Centre.Y ),
                        Colour = secondMiddleColour
                    } );
                }

                quadBatch.Add( new TexturedVertex2D {
                    Position = new Vector2( screenLineLeft.EndPoint.X, screenLineLeft.EndPoint.Y ),
                    TexturePosition = new Vector2( texRect.Left, texRect.Centre.Y ),
                    Colour = colourAt( lineLeft.EndPoint )
                } );
                quadBatch.Add( new TexturedVertex2D {
                    Position = new Vector2( screenLineLeft.StartPoint.X, screenLineLeft.StartPoint.Y ),
                    TexturePosition = new Vector2( texRect.Left, texRect.Centre.Y ),
                    Colour = colourAt( lineLeft.StartPoint )
                } );
            }

            private void updateVertexBuffer () {
                Line line = segments[0];
                float theta = line.Theta;

                // Offset by 0.5 pixels inwards to ensure we never sample texels outside the bounds
                RectangleF texRect = texture.GetTextureRect( new RectangleF( 0.5f, 0.5f, texture.Width - 1, texture.Height - 1 ) );
                addLineCap( line.StartPoint, theta + MathF.PI, MathF.PI, texRect );

                for ( int i = 1; i < segments.Count; ++i ) {
                    Line nextLine = segments[i];
                    float nextTheta = nextLine.Theta;
                    addLineCap( line.EndPoint, theta, nextTheta - theta, texRect );

                    line = nextLine;
                    theta = nextTheta;
                }

                addLineCap( line.EndPoint, theta, MathF.PI, texRect );

                foreach ( Line segment in segments )
                    addLineQuads( segment, texRect );
            }

            public override void Draw ( Action<TexturedVertex2D> vertexAction ) {
                base.Draw( vertexAction );

                if ( texture?.Available != true || segments.Count == 0 )
                    return;

                shader.Bind();

                texture.TextureGL.Bind();

                updateVertexBuffer();

                shader.Unbind();
            }

            protected override void Dispose ( bool isDisposing ) {
                base.Dispose( isDisposing );

                halfCircleBatch.Dispose();
                quadBatch.Dispose();
            }
        }
    }
}
