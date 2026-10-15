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
        private static readonly SolidColorBrush SelectionHandleFill=new SolidColorBrush(Colors.White);
        private static readonly SolidColorBrush SelectionHandleHit=new SolidColorBrush(Colors.Transparent);
        private void UpdateTextHandle(PageView view,Point position,bool anchor)
        {
            var handle=anchor ? view.SelectionStartHandle : view.SelectionEndHandle;
            if(handle==null)
            {
                handle=new Grid { ManipulationMode=ManipulationModes.None, Background=SelectionHandleHit };
                handle.Children.Add(new Ellipse { Fill=SelectionHandleFill, Stroke=SelectionBrush });
                Windows.UI.Xaml.Automation.AutomationProperties.SetName(handle,L.Get("TextSelectionHandle"));
                handle.PointerPressed+=(s,e)=>
                {
                    if(e.Pointer.PointerDeviceType==Windows.Devices.Input.PointerDeviceType.Pen) { ActivatePenInput(); return; }
                    if(currentInkTool!=InkTool.None || !CaptureTextPointer(e)) return;
                    if(anchor)
                    {
                        var page=selectedTextPage; var glyph=selectionAnchor;
                        selectedTextPage=SelectionEndpoint; selectionAnchor=selectionEnd;
                        selectionEndPage=page; selectionEnd=glyph; textSelectionVersion++;
                    }
                    RedrawSelectionPages(); e.Handled=true;
                };
                view.SelectionLayer.Children.Add(handle);
                if(anchor) view.SelectionStartHandle=handle; else view.SelectionEndHandle=handle;
            }
            var zoom=DocumentScroller.ZoomFactor;
            handle.Width=handle.Height=32/zoom;
            var dot=(Ellipse)handle.Children[0]; dot.Width=dot.Height=12/zoom; dot.StrokeThickness=2/zoom;
            Canvas.SetLeft(handle,position.X*view.LayoutWidth-16/zoom);
            Canvas.SetTop(handle,position.Y*view.LayoutHeight-6/zoom);
            handle.Visibility=Visibility.Visible;
        }
    }
}
