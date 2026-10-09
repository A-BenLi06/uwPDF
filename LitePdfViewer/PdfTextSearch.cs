using System;
using System.Collections.Generic;
using System.Text;

namespace LitePdfViewer
{
    internal struct PdfSearchMatch
    {
        public int First, Last;
        public bool Found { get { return First >= 0; } }
        public static PdfSearchMatch Missing { get { return new PdfSearchMatch { First = -1, Last = -1 }; } }
    }

    // Search and query share whitespace normalization. The map keeps selection
    // indices in glyph space even for surrogate pairs, ligatures and line breaks.
    internal static class PdfTextSearch
    {
        public static string NormalizeQuery(string value)
        {
            var result = new StringBuilder();
            foreach (var ch in value ?? string.Empty)
            {
                if (!char.IsWhiteSpace(ch)) result.Append(ch);
                else if (result.Length > 0 && result[result.Length - 1] != ' ') result.Append(' ');
            }
            return result.ToString().Trim();
        }

        public static PdfSearchMatch Find(IReadOnlyList<string> glyphs, string query, int anchor,
            bool forward, out PdfSearchMatch wrapped)
        {
            wrapped = PdfSearchMatch.Missing;
            query = NormalizeQuery(query);
            if (query.Length == 0) return wrapped;
            var text = new StringBuilder();
            var map = new List<int>();
            for (var g = 0; g < glyphs.Count; g++)
            {
                foreach (var ch in glyphs[g])
                {
                    if (char.IsWhiteSpace(ch))
                    {
                        if (text.Length == 0 || text[text.Length - 1] == ' ') continue;
                        text.Append(' ');
                    }
                    else text.Append(ch);
                    map.Add(g);
                }
            }
            var content = text.ToString();
            var result = PdfSearchMatch.Missing;
            for (var at = 0; at <= content.Length - query.Length;)
            {
                    var match = content.IndexOf(query, at, StringComparison.OrdinalIgnoreCase);
                if (match < 0) break;
                var candidate = new PdfSearchMatch { First = map[match], Last = map[match + query.Length - 1] };
                if (!wrapped.Found || !forward) wrapped = candidate;
                if (forward ? candidate.First >= anchor : candidate.Last <= anchor)
                {
                    result = candidate;
                    if (forward) break;
                }
                // Overlapping matches are valid ("ana" occurs twice in "banana").
                at = match + 1;
            }
            return result;
        }
    }
}
