using Windows.Foundation;
using Windows.UI;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Input;
using Windows.UI.Xaml.Media;
using Windows.UI.Xaml.Shapes;

namespace LitePdfViewer
{
    public sealed partial class MainPage
    {
        private void AddTextHandle(PageView view, Point position, int endpoint, int opposite)
        {
            var zoom = DocumentScroller.ZoomFactor;
            var handle = new Grid { Width = 32 / zoom, Height = 32 / zoom, ManipulationMode = ManipulationModes.None, Background = new SolidColorBrush(Colors.Transparent) };
            handle.Children.Add(new Ellipse { Width = 12 / zoom, Height = 12 / zoom,
                Fill = new SolidColorBrush(Colors.White), Stroke = new SolidColorBrush(Color.FromArgb(255, 0, 120, 215)), StrokeThickness = 2 / zoom });
            Canvas.SetLeft(handle, position.X * view.LayoutWidth - 16 / zoom);
            Canvas.SetTop(handle, position.Y * view.LayoutHeight - 6 / zoom);
            Windows.UI.Xaml.Automation.AutomationProperties.SetName(handle, L.Get("TextSelectionHandle"));
            handle.PointerPressed += (s, e) =>
            {
                if (!view.SelectionLayer.CapturePointer(e.Pointer)) return;
                selectionAnchor = opposite; selectionEnd = endpoint;
                selectionPointer = e.Pointer.PointerId;
                selectionIsPen = e.Pointer.PointerDeviceType == Windows.Devices.Input.PointerDeviceType.Pen;
                selectionLastPoint = e.GetCurrentPoint(view.SelectionLayer).Position;
                e.Handled = true;
            };
            view.SelectionLayer.Children.Add(handle);
        }
    }
}
