using System.Globalization;
using Windows.ApplicationModel.Resources;

namespace LitePdfViewer
{
    // Native MRT language matching and fallback are shared with x:Uid and the manifest.
    internal static class L
    {
        internal static string Get(string key)
        {
            return ResourceLoader.GetForViewIndependentUse().GetString(key);
        }

        internal static string Format(string key, params object[] arguments)
        {
            return string.Format(CultureInfo.CurrentUICulture, Get(key), arguments);
        }
    }
}
