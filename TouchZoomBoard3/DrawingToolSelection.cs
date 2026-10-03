namespace TouchZoomBoard
{
    internal static class DrawingToolSelection
    {
        internal static AppMode NormalizeShape(AppMode mode)
        {
            switch (mode)
            {
                case AppMode.Line:
                case AppMode.Arrow:
                case AppMode.Rectangle:
                case AppMode.Ellipse:
                case AppMode.Circle:
                    return mode;
                default:
                    return AppMode.Line;
            }
        }

        internal static AppMode Resolve(DrawingStyleKind kind, AppMode lastShape)
        {
            switch (kind)
            {
                case DrawingStyleKind.Pen: return AppMode.Pen;
                case DrawingStyleKind.Highlighter: return AppMode.Highlighter;
                default: return NormalizeShape(lastShape);
            }
        }
    }
}
