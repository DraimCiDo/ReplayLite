using System;
using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Xml.Linq;

namespace ReplayLite
{
    static class LucideTool
    {
        internal static readonly string[] Names = { "settings", "undo-2", "monitor", "folder", "video", "headphones", "keyboard", "panels-top-left", "external-link", "save", "circle", "log-out", "circle-question-mark", "chevron-down", "chevron-up", "square-play", "pause", "circle-alert", "arrow-up", "check", "download", "x", "minus", "globe", "circle-dot" };
        static double Number(XElement node, string name, double fallback = 0) { var value = node.Attribute(name); return value == null ? fallback : double.Parse(value.Value, CultureInfo.InvariantCulture); }
        static Geometry Shape(XElement node)
        {
            switch (node.Name.LocalName)
            {
                case "path": return Geometry.Parse((string)node.Attribute("d"));
                case "rect": return new RectangleGeometry(new Rect(Number(node, "x"), Number(node, "y"), Number(node, "width"), Number(node, "height")), Number(node, "rx"), Number(node, "ry", Number(node, "rx")));
                case "circle": return new EllipseGeometry(new Point(Number(node, "cx"), Number(node, "cy")), Number(node, "r"), Number(node, "r"));
                case "ellipse": return new EllipseGeometry(new Point(Number(node, "cx"), Number(node, "cy")), Number(node, "rx"), Number(node, "ry"));
                case "line": return new LineGeometry(new Point(Number(node, "x1"), Number(node, "y1")), new Point(Number(node, "x2"), Number(node, "y2")));
                case "polyline": case "polygon":
                    var points = PointCollection.Parse((string)node.Attribute("points")); var path = new PathGeometry(); var figure = new PathFigure { StartPoint = points[0], IsClosed = node.Name.LocalName == "polygon", IsFilled = false };
                    for (int i = 1; i < points.Count; i++) figure.Segments.Add(new LineSegment(points[i], true)); path.Figures.Add(figure); return path;
                default: throw new InvalidDataException("Unsupported Lucide SVG element: " + node.Name.LocalName);
            }
        }
        [STAThread] static int Main(string[] args)
        {
            var visual = new DrawingVisual();
            using (var drawing = visual.RenderOpen())
            {
                var pen = new Pen(Brushes.White, 2) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round, LineJoin = PenLineJoin.Round }; pen.Freeze();
                for (int i = 0; i < Names.Length; i++)
                {
                    var svg = XElement.Load(Path.Combine(args[0], Names[i] + ".svg"));
                    drawing.PushTransform(new TranslateTransform(i * 64, 0)); drawing.PushTransform(new ScaleTransform(64.0 / 24, 64.0 / 24));
                    foreach (var node in svg.Elements()) { var shape = Shape(node); shape.Freeze(); drawing.DrawGeometry(null, pen, shape); }
                    drawing.Pop(); drawing.Pop();
                }
            }
            var bitmap = new RenderTargetBitmap(Names.Length * 64, 64, 96, 96, PixelFormats.Pbgra32); bitmap.Render(visual); bitmap.Freeze();
            var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap)); using (var output = File.Create(args[1])) encoder.Save(output);
            return 0;
        }
    }
}
