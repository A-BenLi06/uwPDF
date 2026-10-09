using System;
using LitePdfViewer;

class PdfTextSearchTests
{
    static void Expect(string[] glyphs, string query, int anchor, bool forward, int first, int last, int wrappedFirst)
    {
        PdfSearchMatch wrapped;
        var match = PdfTextSearch.Find(glyphs, query, anchor, forward, out wrapped);
        if (match.First != first || match.Last != last || wrapped.First != wrappedFirst)
            throw new Exception("Unexpected match for " + query + " at " + anchor + ": " + match.First + "," + match.Last + "; wrapped " + wrapped.First);
    }
    static int Main()
    {
        Expect(new[] { "b", "a", "n", "a", "n", "a" }, "ana", 0, true, 1, 3, 1);
        Expect(new[] { "b", "a", "n", "a", "n", "a" }, "ana", 2, true, 3, 5, 1);
        Expect(new[] { "b", "a", "n", "a", "n", "a" }, "ana", 4, false, 1, 3, 3);
        Expect(new[] { "b", "a", "n", "a", "n", "a" }, "ana", 6, true, -1, -1, 1);
        Expect(new[] { "b", "a", "n", "a", "n", "a" }, "ana", 0, false, -1, -1, 3);
        Expect(new[] { "\n", "A", "\t", " ", "\n", "B", " " }, " a \n b ", 0, true, 1, 5, 1);
        Expect(new[] { "中", "文", "\n", "选", "择" }, "文 选择", 0, true, 1, 4, 1);
        Expect(new[] { "o", "ffi", "c", "e" }, "fi", 0, true, 1, 1, 1);
        Expect(new[] { "x", "\ud83d\ude00", "y" }, "\ud83d\ude00y", 0, true, 1, 2, 1);
        Expect(new[] { "x" }, "missing", 0, true, -1, -1, -1);
        Expect(new string[0], "x", 0, false, -1, -1, -1);
        Expect(new[] { "x" }, " \n ", 0, true, -1, -1, -1);
        Console.WriteLine("PASS: overlap, forward/backward anchors, wrap, whitespace, CJK, ligature, surrogate pair and empty search");
        return 0;
    }
}
