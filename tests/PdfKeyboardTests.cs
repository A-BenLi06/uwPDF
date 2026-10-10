using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Windows.System;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Input;

namespace Windows.System
{
    public enum VirtualKey { Menu=18, Control=17, F=70, O=79, S=83, C=67, A=65,
        Escape=27, Space=32, PageDown=34, Right=39, Down=40, Back=8, PageUp=33,
        Left=37, Up=38, Home=36, End=35, Add=107, Subtract=109, Number0=48, NumberPad0=96 }
}
namespace Windows.UI.Core
{
    [Flags] public enum CoreVirtualKeyStates { None=0, Down=1 }
    public sealed class CoreWindow
    {
        public bool Alt, Control;
        public CoreVirtualKeyStates GetKeyState(VirtualKey key)
        { return (key==VirtualKey.Menu ? Alt : key==VirtualKey.Control && Control) ? CoreVirtualKeyStates.Down : CoreVirtualKeyStates.None; }
    }
}
namespace Windows.UI.Xaml
{
    public sealed class Window
    { public static readonly Window Current=new Window(); public readonly Windows.UI.Core.CoreWindow CoreWindow=new Windows.UI.Core.CoreWindow(); }
}
namespace Windows.UI.Xaml.Controls
{ public class TextBox { } public class PasswordBox { } }
namespace Windows.UI.Xaml.Input
{ public sealed class KeyRoutedEventArgs { public VirtualKey Key; public object OriginalSource; public bool Handled; } }
namespace LitePdfViewer
{
    public sealed class FakeDocument { public uint PageCount=3; }
    public sealed class FakeScroller { public float ZoomFactor=1; }
    public sealed partial class MainPage
    {
        private readonly FakeDocument document=new FakeDocument();
        private uint pageIndex;
        private object selectedTextPage;
        private readonly List<object> pageViews=new List<object>();
        private readonly FakeScroller DocumentScroller=new FakeScroller();
        private ulong activeRenderToken;
        private enum InkTool { None }
        private InkTool currentInkTool;
        public int Navigations, Searches, Saves, Opens, Copies, Selections, Fits, Zooms;
        public uint Page { get { return pageIndex; } }
        public bool Send(VirtualKey key, bool alt=false, bool control=false, object original=null, bool alreadyHandled=false)
        {
            Window.Current.CoreWindow.Alt=alt; Window.Current.CoreWindow.Control=control;
            var e=new KeyRoutedEventArgs { Key=key, OriginalSource=original, Handled=alreadyHandled };
            Page_KeyDown(this,e); return e.Handled;
        }
        public void SelectText() { selectedTextPage=new object(); }
        private bool HasUnsavedChanges() { return false; }
        private void ShowSearch() { Searches++; }
        private Task OpenViaPickerAsync() { Opens++; return Task.FromResult(true); }
        private Task SaveCurrentAnnotationsWithFeedbackAsync() { Saves++; return Task.FromResult(true); }
        private void CopySelectedText() { Copies++; }
        private object GetCurrentPageView() { return null; }
        private Task EnsurePageTextAsync(object page, ulong token) { return Task.FromResult(true); }
        private void SelectAllPageText(object page) { Selections++; }
        private void ClearTextSelection() { selectedTextPage=null; }
        private void GoToPage(uint page) { Navigations++; pageIndex=page; }
        private void FitToWindow(bool animated) { Fits++; }
        private void SetZoom(float zoom, bool animated) { Zooms++; }
    }
}
internal static class PdfKeyboardTests
{
    private static void Check(bool value,string message) { if(!value) throw new Exception(message); }
    public static int Main()
    {
        try
        {
            var page=new LitePdfViewer.MainPage();
            foreach(var key in new[] {VirtualKey.Space,VirtualKey.Right,VirtualKey.Down,VirtualKey.Home,VirtualKey.End,VirtualKey.Add,VirtualKey.Number0})
                Check(!page.Send(key,alt:true),"Alt combination was marked handled: "+key);
            Check(page.Page==0 && page.Navigations==0 && page.Zooms==0 && page.Fits==0,"Alt window shortcuts changed the reading view");
            foreach(var key in new[] {VirtualKey.F,VirtualKey.S,VirtualKey.O})
                Check(!page.Send(key,alt:true,control:true),"Ctrl+Alt combination was consumed");
            Check(page.Searches==0 && page.Saves==0 && page.Opens==0,"Alt combinations invoked document commands");
            Check(page.Send(VirtualKey.Right,alreadyHandled:true) && page.Page==0 && page.Navigations==0,"A control-owned arrow key changed the reading page");
            Check(page.Send(VirtualKey.F,control:true,alreadyHandled:true) && page.Searches==0,"An already handled shortcut escaped its control");
            Check(page.Send(VirtualKey.Space) && page.Page==1,"Plain Space stopped advancing the page");
            Check(page.Send(VirtualKey.PageUp) && page.Page==0,"Plain PageUp stopped navigating backward");
            Check(page.Send(VirtualKey.End) && page.Page==2,"Plain End stopped navigating to the last page");
            Check(page.Send(VirtualKey.Home) && page.Page==0,"Plain Home stopped navigating to the first page");
            Check(page.Send(VirtualKey.F,control:true) && page.Searches==1,"Ctrl+F stopped opening search");
            Check(page.Send(VirtualKey.S,control:true) && page.Saves==1,"Ctrl+S stopped saving annotations");
            Check(page.Send(VirtualKey.O,control:true) && page.Opens==1,"Ctrl+O stopped opening the picker");
            Check(!page.Send(VirtualKey.Space,original:new Windows.UI.Xaml.Controls.TextBox()) && page.Page==0,"TextBox Space escaped into page navigation");
            page.SelectText(); Check(page.Send(VirtualKey.C,control:true) && page.Copies==1,"Ctrl+C stopped copying selected text");
            Check(!page.Send(VirtualKey.C,alt:true,control:true) && page.Copies==1,"Ctrl+Alt+C copied selection unexpectedly");
            Console.WriteLine("PASS: actual keyboard handler leaves Alt shortcuts to the system and retains reader/text shortcuts"); return 0;
        }
        catch(Exception error) { Console.Error.WriteLine(error.Message); return 1; }
    }
}
