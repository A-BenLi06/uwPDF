using Windows.ApplicationModel.Activation;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;

namespace LitePdfViewer
{
    sealed partial class App : Application
    {
        public App()
        {
            InitializeComponent();
            Suspending += OnSuspending;
        }

        protected override void OnLaunched(LaunchActivatedEventArgs e)
        {
            EnsureWindow(e.Arguments);
        }

        protected override void OnFileActivated(FileActivatedEventArgs e)
        {
            EnsureWindow(null);

            var page = Window.Current.Content as MainPage;
            if (page != null && e.Files.Count > 0)
            {
                var file = e.Files[0] as Windows.Storage.StorageFile;
                if (file != null)
                {
                    page.OpenActivatedFile(file);
                }
            }
        }

        protected override void OnActivated(IActivatedEventArgs args)
        {
            EnsureWindow(null);

            if (args.Kind == ActivationKind.Protocol)
            {
                var protocolArgs = args as ProtocolActivatedEventArgs;
                var page = Window.Current.Content as MainPage;
                if (page != null && protocolArgs != null)
                {
                    page.OpenLaunchArguments(protocolArgs.Uri.AbsoluteUri);
                }
            }
        }

        private static void EnsureWindow(string launchArgs)
        {
            var page = Window.Current.Content as MainPage;
            if (page == null)
            {
                page = new MainPage();
                Window.Current.Content = page;
            }

            Window.Current.Activate();

            if (!string.IsNullOrWhiteSpace(launchArgs))
            {
                page.OpenLaunchArguments(launchArgs);
            }
        }

        private void OnSuspending(object sender, Windows.ApplicationModel.SuspendingEventArgs e)
        {
            var deferral = e.SuspendingOperation.GetDeferral();
            // Ink is only written upon explicit manual save, not automatically on suspend.
            deferral.Complete();
        }
    }
}
