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
        private Rect inkSelectionBounds = Rect.Empty, inkGestureBounds, inkGestureFrameBounds;
        private Matrix3x2 inkSelectionTransform = Matrix3x2.Identity, inkGestureFrame;
        private Point inkGestureStart;
        private uint? inkGesturePointer;
        private bool inkGestureMoving;
        private InkSelectionPart inkGesturePart;
        private ManipulationModes? inkViewportManipulation;
        private bool inkGestureTransformed;
        private enum InkSelectionPart { Lasso = -2, Move = -1, TopLeft, Top, TopRight, Right, BottomRight, Bottom, BottomLeft, Left, Rotate }
        private InkMoveUndo pendingInkMove;
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
        private bool HandleInkSelectionKey(Windows.System.VirtualKey key, bool control)
        {
            if (selectedInkPage == null) return false;
            if (key == Windows.System.VirtualKey.Escape)
            {
                if (inkGesturePointer != null)
                {
                    CancelInkGesture();
                    selectedInkPage.InkSelectionLayer.ReleasePointerCaptures();
                }
                else ClearInkSelection();
                return true;
            }
            if (control && key == Windows.System.VirtualKey.Z) { UndoButton_Click(null, null); return true; }
            return false;
        }
        private void UpdateInkSelectionInput(PageView view)
        {
            if (view.InkSelectionLayer == null) return;
            // Explicit lasso has one XAML capture owner for both pen and mouse.
            // Drawing tools retain native UnprocessedInput for barrel selection.
            view.InkSelectionLayer.Background = currentInkTool == InkTool.InkSelect
                ? new SolidColorBrush(Colors.Transparent) : null;
            view.InkSelectionLayer.ManipulationMode = ManipulationModes.System;
        }
        private static bool AcceptInkSelectionPointer(Windows.Devices.Input.PointerDeviceType device,
            bool contact, bool left, bool right, bool eraser)
        {
            // A barrel-held pen is secondary input, not a mouse left-button press.
            return device == Windows.Devices.Input.PointerDeviceType.Pen ? contact && !eraser :
                device == Windows.Devices.Input.PointerDeviceType.Mouse && (left || right);
        }
        private void LockInkManipulation(PageView view)
        {
            if (!inkViewportManipulation.HasValue) inkViewportManipulation = PageFrame.ManipulationMode;
            // UWP 1709+ otherwise turns an active pen drag into ScrollViewer
            // panning and cancels the captured lasso/transform pointer.
            PageFrame.ManipulationMode &= ~ManipulationModes.System;
            view.InkSelectionLayer.ManipulationMode = ManipulationModes.None;
        }
        private void RestoreInkManipulation()
        {
            if (inkViewportManipulation.HasValue) PageFrame.ManipulationMode = inkViewportManipulation.Value;
            inkViewportManipulation = null;
            if (selectedInkPage != null && selectedInkPage.InkSelectionLayer != null)
                selectedInkPage.InkSelectionLayer.ManipulationMode = ManipulationModes.System;
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
                var p = e.CurrentPoint;
                if (currentInkTool == InkTool.None || !AcceptInkSelectionPointer(p.PointerDevice.PointerDeviceType,
                    p.IsInContact, p.Properties.IsLeftButtonPressed, p.Properties.IsRightButtonPressed, p.Properties.IsEraser)) return;
                BeginInkGesture(view, p.Position, p.PointerId);
            };
            input.PointerMoved += (s, e) => UpdateInkGesturePoints(view, e.GetIntermediatePoints(), e.CurrentPoint.PointerId);
            input.PointerReleased += (s, e) => FinishInkGesture(view, e.CurrentPoint.Position, e.CurrentPoint.PointerId);
            input.PointerLost += (s, e) => { if (selectedInkPage == view && inkGesturePointer == e.CurrentPoint.PointerId) CancelInkGesture(); };
            view.InkLayer.InkPresenter.StrokeInput.StrokeStarted += (s, e) => ClearInkSelection();
            layer.PointerPressed += (s, e) =>
            {
                var point = e.GetCurrentPoint(layer);
                if (!AcceptInkSelectionPointer(point.PointerDevice.PointerDeviceType, point.IsInContact,
                    point.Properties.IsLeftButtonPressed, point.Properties.IsRightButtonPressed, point.Properties.IsEraser)) return;
                if (currentInkTool != InkTool.InkSelect && (selectedInkPage != view || HitInkSelection(point.Position) == InkSelectionPart.Lasso)) return;
                if (!view.InkLoaded || view.IsLoadingInk || savingPdf || inkGesturePointer != null) return;
                // Starting a new lasso clears the previous selection and its
                // captures. Capture only afterwards, so that cleanup cannot release
                // the new mouse drag before it has started.
                BeginInkGesture(view, point.Position, e.Pointer.PointerId);
                if (inkGesturePointer != e.Pointer.PointerId) return;
                if (!layer.CapturePointer(e.Pointer)) { CancelInkGesture(); return; }
                Focus(FocusState.Programmatic);
                e.Handled = true;
            };
            layer.PointerMoved += (s, e) =>
            {
                if (inkGesturePointer != e.Pointer.PointerId)
                {
                    if (e.Pointer.PointerDeviceType == Windows.Devices.Input.PointerDeviceType.Mouse &&
                        (currentInkTool == InkTool.InkSelect || selectedInkPage == view))
                        UpdateInkSelectionCursor(selectedInkPage == view ? HitInkSelection(e.GetCurrentPoint(layer).Position) : InkSelectionPart.Lasso);
                    return;
                }
                UpdateInkGesturePoints(view, e.GetIntermediatePoints(layer), e.Pointer.PointerId);
                e.Handled = true;
            };
            layer.PointerReleased += (s, e) =>
            {
                if (inkGesturePointer != e.Pointer.PointerId) return;
                FinishInkGesture(view, e.GetCurrentPoint(layer).Position, e.Pointer.PointerId);
                layer.ReleasePointerCapture(e.Pointer); e.Handled = true;
            };
            layer.PointerCanceled += (s, e) =>
            {
                if (inkGesturePointer != e.Pointer.PointerId) return;
                CancelInkGesture(); layer.ReleasePointerCapture(e.Pointer); e.Handled = true;
            };
            layer.PointerCaptureLost += (s, e) => { if (inkGesturePointer == e.Pointer.PointerId) CancelInkGesture(); };
            layer.PointerExited += (s, e) => { if (inkGesturePointer == null) SetTextCursor(false); };
            layer.RightTapped += (s, e) => { if (currentInkTool == InkTool.InkSelect || selectedInkPage == view) e.Handled = true; };
        }

        private void BeginInkGesture(PageView view, Point point, uint pointer)
        {
            if (savingPdf || inkGesturePointer != null || !view.InkLoaded || view.IsLoadingInk || view.Strokes == null) return;
            ClearTextSelection();
            inkGesturePart = selectedInkPage == view ? HitInkSelection(point) : InkSelectionPart.Lasso;
            inkGestureMoving = inkGesturePart != InkSelectionPart.Lasso;
            if (!inkGestureMoving) { ClearInkSelection(); selectedInkPage = view; }
            inkGestureStart = point; inkGesturePointer = pointer;
            inkGestureTransformed = false; LockInkManipulation(view);
            if (inkGestureMoving)
            {
                inkGestureFrame = inkSelectionTransform;
                inkGestureFrameBounds = InkSelectionFrameBounds();
                inkGestureBounds = SelectedInkBounds(view);
                pendingInkMove = new InkMoveUndo();
                foreach (var stroke in view.Strokes.GetStrokes()) if (stroke.Selected)
                { pendingInkMove.Strokes.Add(stroke); pendingInkMove.Transforms.Add(stroke.PointTransform); }
                UpdateInkSelectionCursor(inkGesturePart);
            }
            else
            {
                inkLasso = new Polyline
                {
                    Stroke = new SolidColorBrush(Color.FromArgb(255, 0, 120, 215)),
                    Fill = new SolidColorBrush(Color.FromArgb(24, 0, 120, 215)),
                    StrokeThickness = 2 / DocumentScroller.ZoomFactor,
                    StrokeDashArray = new DoubleCollection { 3, 2 },
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
            Matrix3x2 delta;
            var distanceX = point.X - inkGestureStart.X; var distanceY = point.Y - inkGestureStart.Y;
            if (!inkGestureTransformed && (distanceX * distanceX + distanceY * distanceY) * DocumentScroller.ZoomFactor * DocumentScroller.ZoomFactor < 4) return;
            inkGestureTransformed = true;
            if (inkGesturePart == InkSelectionPart.Move)
            {
                var dx = Math.Max(-inkGestureBounds.X, Math.Min(Math.Max(0, view.LayoutWidth - inkGestureBounds.Right), point.X - inkGestureStart.X));
                var dy = Math.Max(-inkGestureBounds.Y, Math.Min(Math.Max(0, view.LayoutHeight - inkGestureBounds.Bottom), point.Y - inkGestureStart.Y));
                delta = Matrix3x2.CreateTranslation((float)dx, (float)dy);
                inkSelectionTransform = inkGestureFrame * delta;
            }
            else if (inkGesturePart == InkSelectionPart.Rotate)
            {
                var center = InkTransformPoint(InkRectPoint(inkSelectionBounds, .5, .5), inkGestureFrame);
                var angle = Math.Atan2(point.Y - center.Y, point.X - center.X) - Math.Atan2(inkGestureStart.Y - center.Y, inkGestureStart.X - center.X);
                if (Window.Current.CoreWindow.GetKeyState(Windows.System.VirtualKey.Shift).HasFlag(Windows.UI.Core.CoreVirtualKeyStates.Down))
                    angle = Math.Round(angle / (Math.PI / 12)) * Math.PI / 12;
                delta = Matrix3x2.CreateRotation((float)angle, new Vector2((float)center.X, (float)center.Y));
                inkSelectionTransform = inkGestureFrame * delta;
            }
            else
            {
                Matrix3x2 inverse;
                if (!Matrix3x2.Invert(inkGestureFrame, out inverse)) return;
                var local = InkTransformPoint(point, inverse); var start = InkTransformPoint(inkGestureStart, inverse);
                var index = (int)inkGesturePart;
                var x = InkHandleX[index]; var y = InkHandleY[index];
                var anchor = InkRectPoint(inkGestureFrameBounds, 1 - x, 1 - y);
                var sx = x == .5 ? 1 : (local.X - anchor.X) / (start.X - anchor.X);
                var sy = y == .5 ? 1 : (local.Y - anchor.Y) / (start.Y - anchor.Y);
                if (x != .5 && y != .5)
                {
                    // Corners preserve aspect ratio; edges stretch one axis.
                    var ax = start.X - anchor.X; var ay = start.Y - anchor.Y;
                    sx = sy = ((local.X - anchor.X) * ax + (local.Y - anchor.Y) * ay) / (ax * ax + ay * ay);
                }
                var axisX = Math.Sqrt(inkGestureFrame.M11 * inkGestureFrame.M11 + inkGestureFrame.M12 * inkGestureFrame.M12);
                var axisY = Math.Sqrt(inkGestureFrame.M21 * inkGestureFrame.M21 + inkGestureFrame.M22 * inkGestureFrame.M22);
                var minX = Math.Max(.05 / axisX, 12 / (DocumentScroller.ZoomFactor * inkGestureFrameBounds.Width * axisX));
                var minY = Math.Max(.05 / axisY, 12 / (DocumentScroller.ZoomFactor * inkGestureFrameBounds.Height * axisY));
                var maxX = 32 / axisX; var maxY = 32 / axisY;
                if (x != .5 && y != .5) sx = sy = Math.Max(Math.Max(minX, minY), Math.Min(Math.Min(maxX, maxY), sx));
                else { sx = x == .5 ? 1 : Math.Max(minX, Math.Min(maxX, sx)); sy = y == .5 ? 1 : Math.Max(minY, Math.Min(maxY, sy)); }
                inkSelectionTransform = Matrix3x2.CreateScale((float)sx, (float)sy, new Vector2((float)anchor.X, (float)anchor.Y)) * inkGestureFrame;
                delta = inverse * inkSelectionTransform;
            }
            for (var i = 0; i < pendingInkMove.Strokes.Count; i++)
                pendingInkMove.Strokes[i].PointTransform = pendingInkMove.Transforms[i] * delta;
            DrawInkSelection(view);
        }
        private void FinishInkGesture(PageView view, Point point, uint pointer)
        {
            if (selectedInkPage != view || inkGesturePointer != pointer) return;
            UpdateInkGesture(view, point, pointer);
            inkGesturePointer = null;
            RestoreInkManipulation();
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
                inkSelectionTransform = Matrix3x2.Identity;
                RemoveInkLasso();
            }
            DrawInkSelection(view);
        }
        private static Rect SelectedInkBounds(PageView view)
        {
            var r = Rect.Empty;
            foreach (var stroke in view.Strokes.GetStrokes()) if (stroke.Selected) r.Union(stroke.BoundingRect);
            return r;
        }
        private static Point InkTransformPoint(Point point, Matrix3x2 transform)
        {
            var v = Vector2.Transform(new Vector2((float)point.X, (float)point.Y), transform);
            return new Point(v.X, v.Y);
        }
        private static Point InkRectPoint(Rect bounds, double x, double y)
        {
            return new Point(bounds.X + bounds.Width * x, bounds.Y + bounds.Height * y);
        }
        private void CancelInkGesture()
        {
            if (pendingInkMove != null) { pendingInkMove.Restore(); inkSelectionTransform = inkGestureFrame; }
            pendingInkMove = null; inkGesturePointer = null;
            RestoreInkManipulation();
            RemoveInkLasso();
            if (selectedInkPage != null) DrawInkSelection(selectedInkPage);
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
            if (selectedInkPage != null) SetTextCursor(false);
            selectedInkPage = null; inkSelectionBounds = Rect.Empty; inkSelectionTransform = Matrix3x2.Identity;
            inkSelectionOutline = inkSelectionHalo = null; inkRotationStem = null; inkSelectionHandles = null;
        }
    }
}
