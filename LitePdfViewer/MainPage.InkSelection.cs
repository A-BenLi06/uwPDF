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
        private sealed class InkMoveUndo
        {
            public readonly List<InkStroke> Strokes = new List<InkStroke>();
            public readonly List<Matrix3x2> Transforms = new List<Matrix3x2>();
            public void Restore()
            {
                for (var i = 0; i < Strokes.Count; i++) Strokes[i].PointTransform = Transforms[i];
            }
        }

        private void InkSelectButton_Click(object sender, RoutedEventArgs e) { SetInkTool(InkTool.InkSelect); InkMoreButton.Flyout.Hide(); }
        private void AttachInkSelection(PageView view)
        {
            var layer = new Canvas();
            view.InkSelectionLayer = layer;
            view.Host.Children.Add(layer);
            var input = view.InkLayer.InkPresenter.UnprocessedInput;
            input.PointerPressed += (s, e) =>
            {
                if (e.CurrentPoint.PointerDevice.PointerDeviceType == Windows.Devices.Input.PointerDeviceType.Touch ||
                    !view.InkLoaded || view.IsLoadingInk) return;
                BeginInkGesture(view, e.CurrentPoint.Position, e.CurrentPoint.PointerId);
            };
            input.PointerMoved += (s, e) => UpdateInkGesture(view, e.CurrentPoint.Position, e.CurrentPoint.PointerId);
            input.PointerReleased += (s, e) => FinishInkGesture(view, e.CurrentPoint.Position, e.CurrentPoint.PointerId);
            input.PointerLost += (s, e) => { if (selectedInkPage == view && inkGesturePointer == e.CurrentPoint.PointerId) CancelInkGesture(); };
            view.InkLayer.InkPresenter.StrokeInput.StrokeStarted += (s, e) => ClearInkSelection();
            layer.PointerPressed += (s, e) =>
            {
                var point = e.GetCurrentPoint(layer);
                if (point.PointerDevice.PointerDeviceType == Windows.Devices.Input.PointerDeviceType.Touch || !point.Properties.IsLeftButtonPressed) return;
                if (!layer.CapturePointer(e.Pointer)) return;
                BeginInkGesture(view, point.Position, e.Pointer.PointerId);
                e.Handled = true;
            };
            layer.PointerMoved += (s, e) =>
            {
                if (inkGesturePointer != e.Pointer.PointerId) return;
                UpdateInkGesture(view, e.GetCurrentPoint(layer).Position, e.Pointer.PointerId); e.Handled = true;
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
            else DrawInkSelection(view, new Rect(point, point), false);
        }
        private void UpdateInkGesture(PageView view, Point point, uint pointer)
        {
            if (selectedInkPage != view || inkGesturePointer != pointer) return;
            if (!inkGestureMoving) { DrawInkSelection(view, new Rect(inkGestureStart, point), false); return; }
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
                var r = new Rect(inkGestureStart, point);
                if (r.Width * DocumentScroller.ZoomFactor < 3 || r.Height * DocumentScroller.ZoomFactor < 3) { ClearInkSelection(); return; }
                inkSelectionBounds = view.Strokes.SelectWithPolyLine(new[] {
                    new Point(r.X, r.Y), new Point(r.Right, r.Y), new Point(r.Right, r.Bottom), new Point(r.X, r.Bottom), new Point(r.X, r.Y) });
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
            if (inkSelectionBounds.IsEmpty) return false;
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
            if (selectedInkPage != null) DrawInkSelection(selectedInkPage, inkSelectionBounds, true);
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
