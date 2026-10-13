using Windows.Devices.Input;
using Windows.Foundation;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Controls.Primitives;
using Windows.UI.Xaml.Input;
using Windows.UI.Xaml.Media;

namespace LitePdfViewer
{
    public sealed partial class MainPage
    {
        private Grid touchScrollRoot;
        private ScrollBar touchScrollBar;
        private ScrollViewer touchScrollOwner;
        private uint touchScrollPointer;
        private PdfScrollBarDrag touchScrollDrag;
        private ScrollBar documentVerticalBar;

        private void ScrollBarRoot_Loaded(object sender, RoutedEventArgs e)
        {
            var root = (Grid)sender;
            var bar = VisualTreeHelper.GetParent(root) as ScrollBar;
            if (bar != null && bar.Orientation == Orientation.Vertical && FindScrollOwner(bar) == DocumentScroller) documentVerticalBar = bar;
            if (root.Tag != null) return;
            root.Tag = true;
            // HandledEventsToo keeps native Thumb/RepeatButton routing from
            // swallowing touch. Mouse events still use the native controls.
            root.AddHandler(PointerPressedEvent, new PointerEventHandler(ScrollBarTouchPressed), true);
            root.AddHandler(PointerMovedEvent, new PointerEventHandler(ScrollBarTouchMoved), true);
            root.AddHandler(PointerReleasedEvent, new PointerEventHandler(ScrollBarTouchReleased), true);
            root.AddHandler(PointerCanceledEvent, new PointerEventHandler(ScrollBarTouchReleased), true);
            root.PointerCaptureLost += (s, args) =>
            {
                if (args.OriginalSource == touchScrollRoot && args.Pointer.PointerId == touchScrollPointer) EndTouchScroll();
            };
            root.Unloaded += (s, args) =>
            {
                if (touchScrollRoot == root) EndTouchScroll();
                if (documentVerticalBar == bar) documentVerticalBar = null;
            };
        }

        private void ScrollBarTouchPressed(object sender, PointerRoutedEventArgs e)
        {
            var root = (Grid)sender;
            var point = e.GetCurrentPoint(root);
            if (point.PointerDevice.PointerDeviceType != PointerDeviceType.Touch) return;
            if (touchScrollDrag != null) { e.Handled = true; return; }
            var bar = VisualTreeHelper.GetParent(root) as ScrollBar;
            var thumb = root.FindName("VerticalThumb") as Thumb;
            if (bar == null || bar.Orientation != Orientation.Vertical || thumb == null || bar.Maximum <= bar.Minimum) return;
            var owner = FindScrollOwner(bar);
            if (owner == null) return;
            root.CancelDirectManipulations();
            if (!root.CapturePointer(e.Pointer)) return;
            touchScrollRoot = root;
            touchScrollBar = bar;
            touchScrollOwner = owner;
            touchScrollPointer = e.Pointer.PointerId;
            var top = thumb.TransformToVisual(root).TransformPoint(new Point()).Y;
            touchScrollDrag = new PdfScrollBarDrag(point.Position.Y, bar.Value, bar.Minimum, bar.Maximum,
                root.ActualHeight, top, thumb.ActualHeight);
            VisualStateManager.GoToState(bar, "TouchDragging", true);
            owner.ChangeView(null, touchScrollDrag.ValueAt(point.Position.Y), null, true);
            e.Handled = true;
        }

        private void ScrollBarTouchMoved(object sender, PointerRoutedEventArgs e)
        {
            if (touchScrollDrag == null || sender != touchScrollRoot || e.Pointer.PointerId != touchScrollPointer) return;
            touchScrollOwner.ChangeView(null, touchScrollDrag.ValueAt(e.GetCurrentPoint(touchScrollRoot).Position.Y), null, true);
            e.Handled = true;
        }

        private void ScrollBarTouchReleased(object sender, PointerRoutedEventArgs e)
        {
            if (touchScrollDrag == null || sender != touchScrollRoot || e.Pointer.PointerId != touchScrollPointer) return;
            var root = touchScrollRoot;
            EndTouchScroll();
            root.ReleasePointerCapture(e.Pointer);
            e.Handled = true;
        }

        private void EndTouchScroll()
        {
            if (touchScrollBar != null) VisualStateManager.GoToState(touchScrollBar, "TouchIdle", true);
            var root = touchScrollRoot;
            touchScrollRoot = null; touchScrollBar = null; touchScrollOwner = null; touchScrollDrag = null;
            if (root != null) root.ReleasePointerCaptures();
        }

        private static ScrollViewer FindScrollOwner(DependencyObject child)
        {
            for (var current = VisualTreeHelper.GetParent(child); current != null; current = VisualTreeHelper.GetParent(current))
            {
                var owner = current as ScrollViewer;
                if (owner != null) return owner;
            }
            return null;
        }
    }
}
