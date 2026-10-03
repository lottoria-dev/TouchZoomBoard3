using System;
using System.Collections.Generic;
using System.Linq;

namespace TouchZoomBoard
{
    internal enum PanelToolKind
    {
        Pen, Highlighter, Shapes, Eraser, Undo, Clear, Pointer, MiniMap, EndSession
    }

    internal static class PanelToolLayout
    {
        internal static PanelToolKind[] DefaultOrder => new[]
        {
            PanelToolKind.Pen, PanelToolKind.Highlighter, PanelToolKind.Shapes,
            PanelToolKind.Eraser, PanelToolKind.Undo, PanelToolKind.Clear,
            PanelToolKind.Pointer, PanelToolKind.MiniMap, PanelToolKind.EndSession
        };

        internal static PanelToolKind[] Normalize(IEnumerable<PanelToolKind> order)
        {
            var result = new List<PanelToolKind>();
            foreach (var item in order ?? Enumerable.Empty<PanelToolKind>())
                if (Enum.IsDefined(typeof(PanelToolKind), item) && !result.Contains(item))
                    result.Add(item);
            foreach (var item in DefaultOrder)
                if (!result.Contains(item)) result.Add(item);
            return result.ToArray();
        }

        internal static PanelToolKind[] Parse(string text)
        {
            var result = new List<PanelToolKind>();
            foreach (var token in (text ?? string.Empty).Split(','))
            {
                PanelToolKind item;
                if (Enum.TryParse(token.Trim(), true, out item) &&
                    Enum.IsDefined(typeof(PanelToolKind), item)) result.Add(item);
            }
            return Normalize(result);
        }

        internal static string Serialize(IEnumerable<PanelToolKind> order)
        {
            return string.Join(",", Normalize(order).Select(item => item.ToString()));
        }

        internal static string Title(PanelToolKind item)
        {
            switch (item)
            {
                case PanelToolKind.Pen: return "펜";
                case PanelToolKind.Highlighter: return "형광펜";
                case PanelToolKind.Shapes: return "도형";
                case PanelToolKind.Eraser: return "지우개";
                case PanelToolKind.Undo: return "되돌리기";
                case PanelToolKind.Clear: return "전체 지움";
                case PanelToolKind.Pointer: return "자료 조작";
                case PanelToolKind.MiniMap: return "미니맵 위치";
                default: return "수업 화면 종료";
            }
        }
    }
}
