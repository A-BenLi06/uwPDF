using System;
using Windows.ApplicationModel.DataTransfer;
using Windows.Storage;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;

namespace LitePdfViewer
{
    public sealed partial class MainPage
    {
        private DataTransferManager shareManager;
        private StorageFile requestedShareFile;

        private void InitializeSharing()
        {
            Loaded += (s, e) => RegisterSharing();
            Unloaded += (s, e) =>
            {
                if (shareManager != null)
                {
                    shareManager.DataRequested -= ShareManager_DataRequested;
                    shareManager = null;
                }
                requestedShareFile = null;
            };
        }

        private void RegisterSharing()
        {
            if (shareManager != null) return;
            shareManager = DataTransferManager.GetForCurrentView();
            shareManager.DataRequested += ShareManager_DataRequested;
        }

        private async void ShareButton_Click(object sender, RoutedEventArgs e)
        {
            if (document == null || currentFile == null) return;
            try
            {
                var file = currentFile;
                await SaveCurrentAnnotationsAsync();
                if (file != currentFile) return;
                RegisterSharing();
                requestedShareFile = currentFile;
                DataTransferManager.ShowShareUI();
            }
            catch (Exception ex)
            {
                requestedShareFile = null;
                await new ContentDialog
                {
                    Title = "无法打开分享面板",
                    Content = ex.Message,
                    PrimaryButtonText = "确定"
                }.ShowAsync();
            }
        }

        private async void ShareManager_DataRequested(DataTransferManager sender, DataRequestedEventArgs args)
        {
            var file = requestedShareFile ?? currentFile;
            requestedShareFile = null;
            if (file == null)
            {
                args.Request.FailWithDisplayText("请先打开一个 PDF 文件。");
                return;
            }

            var deferral = args.Request.GetDeferral();
            try
            {
                if (file == currentFile) await SaveCurrentAnnotationsAsync();
                var data = args.Request.Data;
                data.Properties.Title = file.Name;
                data.Properties.Description = "包含已保存批注的 PDF。";
                data.RequestedOperation = DataPackageOperation.Copy;
                // Share the actual file with brokered read-only access, not its path.
                data.SetStorageItems(new IStorageItem[] { file }, true);
            }
            catch (Exception)
            {
                args.Request.FailWithDisplayText("无法保存或分享此 PDF，请重新打开文件后重试。");
            }
            finally { deferral.Complete(); }
        }
    }
}
