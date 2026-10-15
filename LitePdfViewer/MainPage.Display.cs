using System;
using Windows.Graphics.Display;

namespace LitePdfViewer
{
    public sealed partial class MainPage
    {
        private DisplayInformation displayInformation;
        private void StartDisplayMonitoring()
        {
            if (displayInformation != null) return;
            try
            {
                displayInformation = DisplayInformation.GetForCurrentView();
                displayInformation.DpiChanged += DisplayInformation_DpiChanged;
                RefreshDisplayScale(displayInformation.RawPixelsPerViewPixel);
            }
            catch (Exception ex) { System.Diagnostics.Debug.WriteLine("Display scaling: " + ex.Message); }
        }
        private void StopDisplayMonitoring()
        {
            if (displayInformation == null) return;
            displayInformation.DpiChanged -= DisplayInformation_DpiChanged;
            displayInformation = null;
        }
        private void DisplayInformation_DpiChanged(DisplayInformation sender, object args)
        {
            RefreshDisplayScale(sender.RawPixelsPerViewPixel);
        }
        private void RefreshDisplayScale(double scale)
        {
            if (!(scale > 0) || double.IsInfinity(scale) || Math.Abs(scale - dpiScale) < 0.001) return;
            dpiScale = scale;
            ResetRenderFailures();
            WakeRenderer();
        }
    }
}
