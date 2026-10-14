using System;
using System.Runtime.InteropServices;
[ComImport, Guid("45BA127D-10A8-46EA-8AB7-56EA9078943C")] public class ReaderQaActivationManager { }
[ComImport, Guid("2e941141-7f97-4756-ba1d-9decde894a3d"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
public interface IReaderQaActivationManager {
    int ActivateApplication([MarshalAs(UnmanagedType.LPWStr)] string app,
        [MarshalAs(UnmanagedType.LPWStr)] string arguments, uint options, out uint pid);
}
public static class ReaderQaActivation {
    public static uint Launch() {
        uint pid;
        var manager=(IReaderQaActivationManager)new ReaderQaActivationManager();
        Marshal.ThrowExceptionForHR(manager.ActivateApplication("Ben.LitePdfViewerPreview_z6xh1x1kg5n3j!App", "reader-editing-qa", 0, out pid));
        return pid;
    }
}
