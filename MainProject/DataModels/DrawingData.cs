namespace ChaySocial.MainProject.DataModels
{
    /// <summary> One point along a stroke, in the sheet's own coordinates rather than in screen pixels. </summary>
    /// <param name="X"> Distance from the sheet's left edge. </param>
    /// <param name="Y"> Distance from the sheet's top edge. </param>
    public readonly record struct DrawingPoint(int X, int Y);

    /// <summary> One unbroken line: everything drawn between putting the nib down and lifting it again. </summary>
    /// <param name="InkIndex"> Which ink of the palette this stroke is drawn in. </param>
    /// <param name="NibWidthPx"> How wide the nib was, in the sheet's own coordinates. </param>
    /// <param name="Points"> The path, in the order it was drawn. </param>
    public sealed record DrawingStroke(int InkIndex, int NibWidthPx, IReadOnlyList<DrawingPoint> Points);

    /// <summary>
    /// A whole drawing: geometry, not pixels. A sketch is a few hundred bytes, stays sharp at any size, and — because
    /// a stroke keeps the index of its ink rather than a colour — repaints itself when the reader changes theme. The
    /// drawing therefore follows the reader's eyes rather than the writer's.
    /// </summary>
    /// <param name="CanvasWidthPx"> Width the points were drawn against. </param>
    /// <param name="CanvasHeightPx"> Height the points were drawn against. </param>
    /// <param name="Strokes"> The strokes, in the order they were drawn. </param>
    public sealed record DrawingSheet(int CanvasWidthPx, int CanvasHeightPx, IReadOnlyList<DrawingStroke> Strokes)
    {
        /// <summary> Width of the board, and the width every drawing is stored against. </summary>
        /// <remarks>
        /// The board is drawn at exactly this many CSS pixels and never scaled, because a pointer event reports
        /// where it landed in the element's own pixels: the moment the board is stretched or shrunk, those numbers
        /// stop meaning what the sheet's coordinates mean, and correcting for it would need the element's measured
        /// size, which is a JavaScript call this project does not make. So the size is chosen to fit inside the
        /// composer on the narrowest screen the app is checked against — 375 px across, less the page's own 14 px
        /// either side and the card's 20 px — rather than to fill a desktop window. Only the board is fixed: a
        /// finished drawing is geometry and scales to whatever card it lands in.
        /// </remarks>
        public const int DrawingCanvasWidthPx = 280;

        /// <summary> Height of the board, in a ratio that suits a card in a feed. </summary>
        public const int DrawingCanvasHeightPx = 210;

        /// <summary>
        /// Strokes one drawing may hold. High enough that nobody sketching hits it, low enough that a sheet arriving
        /// off the network cannot ask this device to draw for a minute.
        /// </summary>
        public const int MaximumStrokesPerDrawing = 200;

        /// <summary> Points one stroke may hold, for the same reason. </summary>
        public const int MaximumPointsPerStroke = 500;

        /// <summary> The thinnest nib, for outlines and handwriting. </summary>
        public const int DrawingNibThinWidthPx = 2;

        /// <summary> The nib a drawing starts with. </summary>
        public const int DrawingNibMediumWidthPx = 6;

        /// <summary> The widest nib, for filling and for underlining. </summary>
        public const int DrawingNibThickWidthPx = 14;

        /// <summary> True when this sheet is small enough and shaped right to be drawn without further checking. </summary>
        /// <remarks>
        /// Asked of every sheet that arrives from the blob store, because those bytes were written by somebody else's
        /// device: the caps above only bind the board on this one.
        /// </remarks>
        public bool IsDrawable =>
            CanvasWidthPx > 0
            && CanvasHeightPx > 0
            && Strokes.Count <= MaximumStrokesPerDrawing
            && Strokes.All(stroke => stroke.Points.Count is > 0 and <= MaximumPointsPerStroke);
    }
}
