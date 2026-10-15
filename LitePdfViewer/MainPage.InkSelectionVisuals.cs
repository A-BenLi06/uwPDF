using System;
using System.Numerics;
using Windows.Foundation;
using Windows.UI;
using Windows.UI.Core;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Automation;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Media;
using Windows.UI.Xaml.Shapes;

namespace LitePdfViewer
{
    public sealed partial class MainPage
    {
        private static readonly SolidColorBrush InkSelectionBlue = new SolidColorBrush(Color.FromArgb(255, 0, 120, 215));
        private static readonly SolidColorBrush InkSelectionWhite = new SolidColorBrush(Colors.White);
        private static readonly SolidColorBrush InkSelectionTransparent = new SolidColorBrush(Colors.Transparent);
        private static readonly double[] InkHandleX = { 0, .5, 1, 1, 1, .5, 0, 0 };
        private static readonly double[] InkHandleY = { 0, 0, 0, .5, 1, 1, 1, .5 };
        private Polygon inkSelectionOutline, inkSelectionHalo;
        private Line inkRotationStem;
        private Grid[] inkSelectionHandles;
        private readonly Point[] inkHandlePositions = new Point[9];
        private static readonly CoreCursor InkLassoCursor = new CoreCursor(CoreCursorType.Cross, 0);
        private static readonly CoreCursor InkMoveCursor = new CoreCursor(CoreCursorType.SizeAll, 0);
        private static readonly CoreCursor InkRotateCursor = new CoreCursor(CoreCursorType.Hand, 0);
        private static readonly CoreCursor InkDiagonalCursor = new CoreCursor(CoreCursorType.SizeNorthwestSoutheast, 0);
        private static readonly CoreCursor InkOtherDiagonalCursor = new CoreCursor(CoreCursorType.SizeNortheastSouthwest, 0);
        private static readonly CoreCursor InkVerticalCursor = new CoreCursor(CoreCursorType.SizeNorthSouth, 0);
        private static readonly CoreCursor InkHorizontalCursor = new CoreCursor(CoreCursorType.SizeWestEast, 0);

        private Rect InkSelectionFrameBounds()
        {
            if (inkSelectionBounds.IsEmpty) return Rect.Empty;
            var xScale = Math.Sqrt(inkSelectionTransform.M11 * inkSelectionTransform.M11 + inkSelectionTransform.M12 * inkSelectionTransform.M12);
            var yScale = Math.Sqrt(inkSelectionTransform.M21 * inkSelectionTransform.M21 + inkSelectionTransform.M22 * inkSelectionTransform.M22);
            var width = Math.Max(inkSelectionBounds.Width + 16 / (DocumentScroller.ZoomFactor * xScale), 56 / (DocumentScroller.ZoomFactor * xScale));
            var height = Math.Max(inkSelectionBounds.Height + 16 / (DocumentScroller.ZoomFactor * yScale), 56 / (DocumentScroller.ZoomFactor * yScale));
            // Even one thin stroke needs room between its edge grips to move it.
            return new Rect(inkSelectionBounds.X - (width - inkSelectionBounds.Width) / 2,
                inkSelectionBounds.Y - (height - inkSelectionBounds.Height) / 2, width, height);
        }

        private InkSelectionPart HitInkSelection(Point point)
        {
            if (inkSelectionBounds.IsEmpty || inkSelectionBounds.Width <= 0 || inkSelectionBounds.Height <= 0) return InkSelectionPart.Lasso;
            var radius = 14.0 / DocumentScroller.ZoomFactor;
            var closest = radius * radius; var part = InkSelectionPart.Lasso;
            for (var i = 0; i < inkHandlePositions.Length; i++)
            {
                var dx = point.X - inkHandlePositions[i].X; var dy = point.Y - inkHandlePositions[i].Y;
                var distance = dx * dx + dy * dy;
                if (distance < closest) { closest = distance; part = (InkSelectionPart)i; }
            }
            if (part != InkSelectionPart.Lasso) return part;
            Matrix3x2 inverse;
            return Matrix3x2.Invert(inkSelectionTransform, out inverse) && InkSelectionFrameBounds().Contains(InkTransformPoint(point, inverse))
                ? InkSelectionPart.Move : InkSelectionPart.Lasso;
        }

        private void UpdateInkSelectionCursor(InkSelectionPart part)
        {
            CoreCursor cursor;
            switch (part)
            {
                case InkSelectionPart.Move: cursor = InkMoveCursor; break;
                case InkSelectionPart.Rotate: cursor = InkRotateCursor; break;
                case InkSelectionPart.TopLeft: case InkSelectionPart.BottomRight: cursor = InkDiagonalCursor; break;
                case InkSelectionPart.TopRight: case InkSelectionPart.BottomLeft: cursor = InkOtherDiagonalCursor; break;
                case InkSelectionPart.Top: case InkSelectionPart.Bottom: cursor = InkVerticalCursor; break;
                case InkSelectionPart.Left: case InkSelectionPart.Right: cursor = InkHorizontalCursor; break;
                default: cursor = InkLassoCursor; break;
            }
            Window.Current.CoreWindow.PointerCursor = cursor;
        }

        private void DrawInkSelection(PageView view)
        {
            if (view.InkSelectionLayer == null) return;
            var valid = !inkSelectionBounds.IsEmpty && inkSelectionBounds.Width > 0 && inkSelectionBounds.Height > 0;
            if (!valid) return;
            var layer = view.InkSelectionLayer;
            if (inkSelectionOutline == null)
            {
                inkSelectionHalo = new Polygon { Stroke = InkSelectionWhite, IsHitTestVisible = false };
                inkSelectionOutline = new Polygon { Stroke = InkSelectionBlue, Fill = new SolidColorBrush(Color.FromArgb(24, 0, 120, 215)) };
                for (var i = 0; i < 4; i++) { inkSelectionHalo.Points.Add(new Point()); inkSelectionOutline.Points.Add(new Point()); }
                layer.Children.Add(inkSelectionHalo); layer.Children.Add(inkSelectionOutline);
                AutomationProperties.SetName(inkSelectionOutline, L.Get("InkSelection"));
                ToolTipService.SetToolTip(inkSelectionOutline, L.Get("InkSelectionHelp"));
                inkRotationStem = new Line { Stroke = InkSelectionBlue, IsHitTestVisible = false };
                layer.Children.Add(inkRotationStem);
                inkSelectionHandles = new Grid[9];
                for (var i = 0; i < 9; i++)
                {
                    var handle = new Grid { Background = InkSelectionTransparent };
                    handle.Children.Add(new Ellipse { Fill = InkSelectionWhite, Stroke = InkSelectionBlue });
                    var label = L.Get(i == 8 ? "InkRotateHandle" : i % 2 == 0 ? "InkScaleHandle" : "InkStretchHandle");
                    AutomationProperties.SetName(handle, label); ToolTipService.SetToolTip(handle, label);
                    if (i == 8) handle.Children.Add(new TextBlock { Text = "\uE7AD", FontFamily = new FontFamily("Segoe MDL2 Assets"),
                        Foreground = InkSelectionBlue, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center });
                    inkSelectionHandles[i] = handle; layer.Children.Add(handle);
                }
            }
            var zoom = DocumentScroller.ZoomFactor;
            var bounds = InkSelectionFrameBounds();
            for (var i = 0; i < 8; i++) inkHandlePositions[i] = InkTransformPoint(InkRectPoint(bounds, InkHandleX[i], InkHandleY[i]), inkSelectionTransform);
            // Flip the rotation grip towards the page interior at a page edge.
            var top = inkHandlePositions[1]; var bottom = inkHandlePositions[5];
            var dx = top.X - bottom.X; var dy = top.Y - bottom.Y;
            var length = Math.Sqrt(dx * dx + dy * dy); var offset = 34 / zoom;
            var rotation = new Point(top.X + offset * dx / length, top.Y + offset * dy / length);
            var stem = top; var margin = 14 / zoom;
            if (rotation.X < margin || rotation.Y < margin || rotation.X > view.LayoutWidth - margin || rotation.Y > view.LayoutHeight - margin)
            { stem = bottom; rotation = new Point(bottom.X - offset * dx / length, bottom.Y - offset * dy / length); }
            inkHandlePositions[8] = rotation;
            inkRotationStem.X1 = stem.X; inkRotationStem.Y1 = stem.Y; inkRotationStem.X2 = rotation.X; inkRotationStem.Y2 = rotation.Y;
            inkRotationStem.StrokeThickness = 1.5 / zoom;
            for (var i = 0; i < 4; i++)
            { inkSelectionOutline.Points[i] = inkSelectionHalo.Points[i] = inkHandlePositions[i * 2]; }
            inkSelectionHalo.StrokeThickness = 4 / zoom; inkSelectionOutline.StrokeThickness = 1.5 / zoom;
            for (var i = 0; i < 9; i++)
            {
                var handle = inkSelectionHandles[i]; var diameter = (i == 8 ? 24 : 10) / zoom;
                handle.Width = handle.Height = 28 / zoom;
                var dot = (Ellipse)handle.Children[0]; dot.Width = dot.Height = diameter; dot.StrokeThickness = 1.5 / zoom;
                if (i == 8) ((TextBlock)handle.Children[1]).FontSize = 14 / zoom;
                Canvas.SetLeft(handle, inkHandlePositions[i].X - 14 / zoom); Canvas.SetTop(handle, inkHandlePositions[i].Y - 14 / zoom);
            }
        }
    }
}
