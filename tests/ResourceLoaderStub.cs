using System;
using System.IO;
using System.Linq;
using System.Xml.Linq;

namespace Windows.ApplicationModel.Resources
{
    // Test-only platform boundary: read the shipped RESW; production uses MRT.
    internal sealed class ResourceLoader
    {
        public static string TestLanguage = "zh-Hans";
        public static ResourceLoader GetForViewIndependentUse() { return new ResourceLoader(); }
        public string GetString(string key)
        {
            var root = new DirectoryInfo(AppDomain.CurrentDomain.BaseDirectory);
            while (root != null && !Directory.Exists(Path.Combine(root.FullName, "LitePdfViewer", "Strings"))) root = root.Parent;
            if (root == null) throw new Exception("Could not locate shipped language resources.");
            var doc = XDocument.Load(Path.Combine(root.FullName, "LitePdfViewer", "Strings", TestLanguage, "Resources.resw"));
            return doc.Root.Elements("data").Single(entry => (string)entry.Attribute("name") == key).Element("value").Value;
        }
    }
}
