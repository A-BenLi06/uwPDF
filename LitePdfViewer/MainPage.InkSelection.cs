using System;
using System.Collections.Generic;
using System.Numerics;
using Windows.Foundation;
using Windows.UI;
using Windows.UI.Input.Inking;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Input;
using Windows.UI.Xaml.Media;
using Windows.UI.Xaml.Shapes;

namespace LitePdfViewer
{
    public sealed partial class MainPage
    {
        private PageView selectedInkPage;
        private Rect inkSelectionBounds;
        private Point inkGestureStart;
        private uint? inkGesturePointer;
        private bool inkGestureMoving;
        private InkMoveUndo pendingInkMove;
        private Rectangle inkSelectionRectangle;
        private Polyline inkLasso;
        private sealed class InkMoveUndo
        {
            public readonly List<InkStroke> Strokes = new List<InkStroke>();
            public readonly List<Matrix3x2> Transforms = new List<Matrix3x2>();
            public void Restore()
            {
                for (var i = 0; i < Strokes.Count; i++) Strokes[i].PointTransform = Transforms[i];
            }
        }

        private void InkSelectButton_Click(object sender, RoutedEventArgs e) { SetInkTool(InkTool.InkSelect); }
        private void UpdateInkSelectionInput(PageView view)
        {
            if (view.InkSelectionLayer == null) return;
            // A transparent background receives primary mouse input anywhere
            // on the page. The ordinary pen path stays in UnprocessedInput.
            view.InkSelectionLayer.Background = currentInkTool == InkTool.InkSelect
                ? new SolidColorBrush(Colors.Transparent) : null;
            view.InkSelectionLayer.ManipulationMode = ManipulationModes.System;
        }
        private void AttachInkSelection(PageView view)
        {
            var layer = new Canvas();
            view.InkSelectionLayer = layer;
            view.Host.Children.Add(layer);
            UpdateInkSelectionInput(view);
            var input = view.InkLayer.InkPresenter.UnprocessedInput;
            input.PointerPressed += (s, e) =>
            {
                if (e.CurrentPoint.PointerDevice.PointerDeviceType == Windows.Devices.Input.PointerDeviceType.Touch ||
                    !view.InkLoaded || view.IsLoadingInk) return;
                BeginInkGesture(view, e.CurrentPoint.Position, e.CurrentPoint.PointerId);
            };
            input.PointerMoved += (s, e) => UpdateInkGesturePoints(view, e.GetIntermediatePoints(), e.CurrentPoint.PointerId);
            input.PointerReleased += (s, e) => FinishInkGesture(view, e.CurrentPoint.Position, e.CurrentPoint.PointerId);
            input.PointerLost += (s, e) => { if (selectedInkPage == view && inkGesturePointer == e.CurrentPoint.PointerId) CancelInkGesture(); };
            view.InkLayer.InkPresenter.StrokeInput.StrokeStarted += (s, e) => ClearInkSelection();
            layer.PointerPressed += (s, e) =>
            {
                var point = e.GetCurrentPoint(layer);
                if (point.PointerDevice.PointerDeviceType == Windows.Devices.Input.PointerDeviceType.Touch || !point.Properties.IsLeftButtonPressed) return;
                if (currentInkTool != InkTool.InkSelect && !InkSelectionContains(point.Position)) return;
                if (!view.InkLoaded || view.IsLoadingInk || savingPdf || inkGesturePointer != null) return;
                // Starting a new lasso clears the previous selection and its
                // captures. Capture only afterwards, so that cleanup cannot release
                // the new mouse drag before it has started.
                BeginInkGesture(view, point.Position, e.Pointer.PointerId);
                if (!layer.CapturePointer(e.Pointer)) { CancelInkGesture(); return; }
                e.Handled = true;
            };
            layer.PointerMoved += (s, e) =>
            {
                if (inkGesturePointer != e.Pointer.PointerId) return;
                UpdateInkGesturePoints(view, e.GetIntermediatePoints(layer), e.Pointer.PointerId);
                e.Handled = true;
            };
            layer.PointerReleased += (s, e) =>
            {
                if (inkGesturePointer != e.Pointer.PointerId) return;
                FinishInkGesture(view, e.GetCurrentPoint(layer).Position, e.Pointer.PointerId);
                layer.ReleasePointerCapture(e.Pointer); e.Handled = true;
            };
            layer.PointerCanceled += (s, e) => { CancelInkGesture(); layer.ReleasePointerCapture(e.Pointer); };
            layer.PointerCaptureLost += (s, e) => { if (inkGesturePointer == e.Pointer.PointerId) CancelInkGesture(); };
        }

        private void BeginInkGesture(PageView view, Point point, uint pointer)
        {
            if (savingPdf || inkGesturePointer != null) return;
            ClearTextSelection();
            inkGestureMoving = selectedInkPage == view && InkSelectionContains(point);
            if (!inkGestureMoving) { ClearInkSelection(); selectedInkPage = view; }
            inkGestureStart = point; inkGesturePointer = pointer;
            if (inkGestureMoving)
            {
                pendingInkMove = new InkMoveUndo();
                foreach (var stroke in view.Strokes.GetStrokes()) if (stroke.Selected)
                { pendingInkMove.Strokes.Add(stroke); pendingInkMove.Transforms.Add(stroke.PointTransform); }
            }
            else
            {
                inkLasso = new Polyline
                {
                    Stroke = new SolidColorBrush(Color.FromArgb(255, 0, 120, 215)),
                    StrokeThickness = 1 / DocumentScroller.ZoomFactor,
                    StrokeDashArray = new DoubleCollection { 4, 3 },
                    StrokeLineJoin = PenLineJoin.Round,
                    IsHitTestVisible = false
                };
                inkLasso.Points.Add(point);
                view.InkSelectionLayer.Children.Add(inkLasso);
            }
        }
        private void UpdateInkGesturePoints(PageView view, IList<Windows.UI.Input.PointerPoint> points, uint pointer)
        {
            if (selectedInkPage != view || inkGesturePointer != pointer || points.Count == 0) return;
            // Use timestamp order for both XAML and InkPresenter histories. Fast
            // mouse and pen loops must retain their bends between UI callbacks.
            var reverse = points[0].Timestamp > points[points.Count - 1].Timestamp;
            if (inkGestureMoving)
                UpdateInkGesture(view, points[reverse ? 0 : points.Count - 1].Position, pointer);
            else
                for (var i = 0; i < points.Count; i++)
                    UpdateInkGesture(view, points[reverse ? points.Count - 1 - i : i].Position, pointer);
        }
        private void UpdateInkGesture(PageView view, Point point, uint pointer)
        {
            if (selectedInkPage != view || inkGesturePointer != pointer) return;
            if (!inkGestureMoving)
            {
                var last = inkLasso.Points[inkLasso.Points.Count - 1];
                var lassoDx = point.X - last.X; var lassoDy = point.Y - last.Y;
                var spacing = 1.5 / DocumentScroller.ZoomFactor;
                if (lassoDx * lassoDx + lassoDy * lassoDy >= spacing * spacing) inkLasso.Points.Add(point);
                return;
            }
            var dx = Math.Max(-inkSelectionBounds.X, Math.Min(view.LayoutWidth - inkSelectionBounds.Right, point.X - inkGestureStart.X));
            var dy = Math.Max(-inkSelectionBounds.Y, Math.Min(view.LayoutHeight - inkSelectionBounds.Bottom, point.Y - inkGestureStart.Y));
            for (var i = 0; i < pendingInkMove.Strokes.Count; i++)
            {
                var transform = pendingInkMove.Transforms[i];
                transform.M31 += (float)dx; transform.M32 += (float)dy;
                pendingInkMove.Strokes[i].PointTransform = transform;
            }
            DrawInkSelection(view, new Rect(inkSelectionBounds.X + dx, inkSelectionBounds.Y + dy, inkSelectionBounds.Width, inkSelectionBounds.Height), true);
        }
        private void FinishInkGesture(PageView view, Point point, uint pointer)
        {
            if (selectedInkPage != view || inkGesturePointer != pointer) return;
            UpdateInkGesture(view, point, pointer);
            inkGesturePointer = null;
            if (inkGestureMoving)
            {
                var changed = false;
                for (var i = 0; i < pendingInkMove.Strokes.Count; i++)
                    changed |= pendingInkMove.Strokes[i].PointTransform != pendingInkMove.Transforms[i];
                if (changed)
                {
                    view.InkMoves.Add(pendingInkMove); view.UndoKinds.Add(2); MarkPageDirty(view);
                }
                pendingInkMove = null;
                inkSelectionBounds = SelectedInkBounds(view);
            }
            else
            {
                // Close the actual freehand path, including when the pointer was
                // released a little short of the first point. Never replace it
                // with its bounding rectangle: concave loops must stay concave.
                inkLasso.Points.Add(point);
                if (inkLasso.Points.Count < 4) { ClearInkSelection(); return; }
                inkLasso.Points.Add(inkLasso.Points[0]);
                inkSelectionBounds = view.Strokes.SelectWithPolyLine(inkLasso.Points);
                RemoveInkLasso();
            }
            DrawInkSelection(view, inkSelectionBounds, true);
        }
        private static Rect SelectedInkBounds(PageView view)
        {
            var r = Rect.Empty;
            foreach (var stroke in view.Strokes.GetStrokes()) if (stroke.Selected) r.Union(stroke.BoundingRect);
            return r;
        }
        private bool InkSelectionContains(Point point)
        {
            if (inkSelectionBounds.IsEmpty || inkSelectionBounds.Width <= 0 || inkSelectionBounds.Height <= 0) return false;
            var padding = 6 / DocumentScroller.ZoomFactor;
            return new Rect(inkSelectionBounds.X - padding, inkSelectionBounds.Y - padding,
                inkSelectionBounds.Width + 2 * padding, inkSelectionBounds.Height + 2 * padding).Contains(point);
        }
        private void DrawInkSelection(PageView view, Rect bounds, bool selected)
        {
            if (view.InkSelectionLayer == null) return;
            if (inkSelectionRectangle == null)
            {
                inkSelectionRectangle = new Rectangle { Stroke = new SolidColorBrush(Color.FromArgb(255, 0, 120, 215)),
                    Fill = new SolidColorBrush(Color.FromArgb(10, 0, 120, 215)), StrokeDashArray = new DoubleCollection { 4, 3 } };
                view.InkSelectionLayer.Children.Add(inkSelectionRectangle);
                Windows.UI.Xaml.Automation.AutomationProperties.SetName(inkSelectionRectangle, L.Get("InkSelection"));
            }
            var valid = !bounds.IsEmpty && bounds.Width > 0 && bounds.Height > 0;
            inkSelectionRectangle.Visibility = valid ? Visibility.Visible : Visibility.Collapsed;
            if (!valid) return;
            if (selected)
            {
                var padding = 6 / DocumentScroller.ZoomFactor;
                bounds = new Rect(bounds.X - padding, bounds.Y - padding, bounds.Width + 2 * padding, bounds.Height + 2 * padding);
            }
            inkSelectionRectangle.StrokeThickness = 1 / DocumentScroller.ZoomFactor;
            inkSelectionRectangle.IsHitTestVisible = selected;
            inkSelectionRectangle.Width = bounds.Width; inkSelectionRectangle.Height = bounds.Height;
            Canvas.SetLeft(inkSelectionRectangle, bounds.X); Canvas.SetTop(inkSelectionRectangle, bounds.Y);
            ToolTipService.SetToolTip(inkSelectionRectangle, L.Get("InkSelectionHelp"));
        }
        private void CancelInkGesture()
        {
            if (pendingInkMove != null) pendingInkMove.Restore();
            pendingInkMove = null; inkGesturePointer = null;
            RemoveInkLasso();
            if (selectedInkPage != null) DrawInkSelection(selectedInkPage, inkSelectionBounds, true);
        }
        private void RemoveInkLasso()
        {
            if (inkLasso != null && selectedInkPage != null && selectedInkPage.InkSelectionLayer != null)
                selectedInkPage.InkSelectionLayer.Children.Remove(inkLasso);
            inkLasso = null;
        }
        private void ClearInkSelection()
        {
            CancelInkGesture();
            if (selectedInkPage != null)
            {
                if (selectedInkPage.Strokes != null) foreach (var stroke in selectedInkPage.Strokes.GetStrokes()) stroke.Selected = false;
                if (selectedInkPage.InkSelectionLayer != null)
                { selectedInkPage.InkSelectionLayer.ReleasePointerCaptures(); selectedInkPage.InkSelectionLayer.Children.Clear(); }
            }
            selectedInkPage = null; inkSelectionRectangle = null; inkSelectionBounds = Rect.Empty;
        }
    }
}
