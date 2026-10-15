using Windows.Devices.Input;
using Windows.Foundation;
using Windows.UI.Core;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Controls.Primitives;
using Windows.UI.Xaml.Media;
using Windows.UI.Xaml.Input;

namespace LitePdfViewer
{
    public sealed partial class MainPage
    {
        private ScrollBar documentVerticalBar;
        private NativeScrollIndicator documentScrollIndicator, thumbnailScrollIndicator;

        private void InitializeNativeScrollBars()
        {
            documentScrollIndicator = new NativeScrollIndicator(DocumentScroller, MainChrome);
            thumbnailScrollIndicator = new NativeScrollIndicator(ThumbnailScroller, MainChrome);
            DocumentScroller.Loaded += (sender, args) =>
            {
                documentVerticalBar = documentScrollIndicator.Bar;
            };
        }

        // Only native display states change. Range, template, geometry, capture,
        // mouse dragging, keyboard input and direct touch panning remain native.
        private sealed class NativeScrollIndicator
        {
            private readonly ScrollViewer owner;
            internal ScrollBar Bar { get; private set; }
            private VisualStateGroup indicators, conscious;
            private Thumb thumb;
            private bool near, dragging, applying, loaded, stateUpdateQueued;
            private string expandedState = "MouseIndicator";

            internal NativeScrollIndicator(ScrollViewer scroller, FrameworkElement proximityRoot)
            {
                owner = scroller;
                owner.Loaded += (sender, args) => { loaded = true; BindTemplate(); };
                owner.Unloaded += (sender, args) => { loaded = false; DetachTemplate(); };
                owner.LayoutUpdated += (sender, args) => { if (loaded && Bar == null) BindTemplate(); };
                proximityRoot.AddHandler(UIElement.PointerMovedEvent, new PointerEventHandler(PointerMoved), true);
                proximityRoot.AddHandler(UIElement.PointerEnteredEvent, new PointerEventHandler(PointerMoved), true);
                proximityRoot.AddHandler(UIElement.PointerExitedEvent, new PointerEventHandler(PointerExited), true);
                proximityRoot.AddHandler(UIElement.PointerPressedEvent, new PointerEventHandler((sender, args) =>
                {
                    if (args.Pointer.PointerDeviceType == PointerDeviceType.Touch) SetNear(false);
                }), true);
            }

            private void BindTemplate()
            {
                DetachTemplate();
                owner.ApplyTemplate();
                Bar = FindNativeVerticalScrollBar(owner);
                if (Bar == null) return;
                indicators = FindGroup(owner, "ScrollingIndicatorStates");
                expandedState = HasState(indicators, "MouseIndicatorFull") ? "MouseIndicatorFull" : "MouseIndicator";
                conscious = FindGroup(Bar, "ConsciousStates");
                if (indicators != null) indicators.CurrentStateChanged += StateChanged;
                if (conscious != null) conscious.CurrentStateChanged += StateChanged;
                thumb = FindThumb(Bar);
                if (thumb != null) { thumb.DragStarted += DragStarted; thumb.DragCompleted += DragCompleted; }
                Bar.Unloaded += BarUnloaded;
                ApplyStates();
            }

            private void BarUnloaded(object sender, RoutedEventArgs args) { DetachTemplate(); }
            private void DetachTemplate()
            {
                if (indicators != null) indicators.CurrentStateChanged -= StateChanged;
                if (conscious != null) conscious.CurrentStateChanged -= StateChanged;
                if (thumb != null) { thumb.DragStarted -= DragStarted; thumb.DragCompleted -= DragCompleted; }
                if (Bar != null) Bar.Unloaded -= BarUnloaded;
                indicators = conscious = null; thumb = null; Bar = null; dragging = false;
            }

            private void PointerMoved(object sender, PointerRoutedEventArgs args)
            {
                var point = args.GetCurrentPoint(owner);
                if (point.PointerDevice.PointerDeviceType == PointerDeviceType.Mouse ||
                    (point.PointerDevice.PointerDeviceType == PointerDeviceType.Pen && !point.IsInContact))
                    UpdateProximity(point.Position);
            }

            private void PointerExited(object sender, PointerRoutedEventArgs args)
            {
                if (args.Pointer.PointerDeviceType == PointerDeviceType.Touch) return;
                if (args.OriginalSource == sender) SetNear(false);
                else UpdateProximity(args.GetCurrentPoint(owner).Position);
            }

            internal void UpdateProximity(Point point)
            {
                if (Bar == null) return;
                var origin = Bar.TransformToVisual(owner).TransformPoint(new Point());
                // Enter within 24 DIP of the track; leave at 36 DIP to avoid
                // flicker at the boundary. The detection surface captures no input.
                var distance = near ? 36 : 24;
                SetNear(point.X >= origin.X - distance && point.X <= origin.X + Bar.ActualWidth + distance &&
                    point.Y >= origin.Y && point.Y <= origin.Y + Bar.ActualHeight);
            }

            internal void SetNear(bool value) { near = value; ApplyStates(); }
            private void DragStarted(object sender, DragStartedEventArgs args) { dragging = true; ApplyStates(); }
            private void DragCompleted(object sender, DragCompletedEventArgs args) { dragging = false; ApplyStates(); }
            private void StateChanged(object sender, VisualStateChangedEventArgs args)
            {
                if (applying || stateUpdateQueued) return;
                // Re-entering GoToState from the native state callback can be
                // overwritten by that transition's final commit. Apply once
                // after the native transition returns, with no polling timer.
                stateUpdateQueued = true;
                var update = owner.Dispatcher.RunAsync(CoreDispatcherPriority.Normal, () =>
                {
                    stateUpdateQueued = false;
                    if (loaded) ApplyStates();
                });
            }

            private void ApplyStates()
            {
                if (applying || Bar == null) return;
                applying = true;
                try
                {
                    var expanded = near || dragging;
                    var wanted = expanded ? expandedState : "TouchIndicator";
                    if (indicators != null && (indicators.CurrentState == null || indicators.CurrentState.Name != wanted))
                        VisualStateManager.GoToState(owner, wanted, true);
                    wanted = expanded ? "Expanded" : "Collapsed";
                    if (conscious != null && (conscious.CurrentState == null || conscious.CurrentState.Name != wanted))
                        VisualStateManager.GoToState(Bar, wanted, true);
                }
                finally { applying = false; }
            }

            private static bool HasState(VisualStateGroup group, string name)
            {
                if (group != null) foreach (var state in group.States) if (state.Name == name) return true;
                return false;
            }

            private static VisualStateGroup FindGroup(DependencyObject control, string name)
            {
                for (int i = 0; i < VisualTreeHelper.GetChildrenCount(control); i++)
                {
                    var child = VisualTreeHelper.GetChild(control, i);
                    var root = child as FrameworkElement;
                    if (root != null) foreach (var group in VisualStateManager.GetVisualStateGroups(root))
                        if (group.Name == name) return group;
                    if (child is ScrollBar || child is ScrollContentPresenter) continue;
                    var match = FindGroup(child, name);
                    if (match != null) return match;
                }
                return null;
            }

            private static Thumb FindThumb(DependencyObject control)
            {
                for (int i = 0; i < VisualTreeHelper.GetChildrenCount(control); i++)
                {
                    var child = VisualTreeHelper.GetChild(control, i);
                    var found = child as Thumb;
                    if (found != null && found.Name == "VerticalThumb") return found;
                    found = FindThumb(child);
                    if (found != null) return found;
                }
                return null;
            }
        }

        private static ScrollBar FindNativeVerticalScrollBar(DependencyObject element)
        {
            var bar = element as ScrollBar;
            if (bar != null && bar.Name == "VerticalScrollBar" && bar.Orientation == Orientation.Vertical) return bar;
            for (int i = 0; i < VisualTreeHelper.GetChildrenCount(element); i++)
            {
                var found = FindNativeVerticalScrollBar(VisualTreeHelper.GetChild(element, i));
                if (found != null) return found;
            }
            return null;
        }
    }
}
