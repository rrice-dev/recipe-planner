using System.Text.RegularExpressions;

namespace RecipePlanner.Api.Import;

// The readable text of a page, for the AI fallback: no scripts, styles, menus or markup
public static partial class PageText
{
    [GeneratedRegex(@"<(script|style|noscript|svg|template|iframe|nav|footer|header|form)\b[^>]*>.*?</\1\s*>", RegexOptions.IgnoreCase | RegexOptions.Singleline)]
    private static partial Regex Hidden();

    [GeneratedRegex(@"<!--.*?-->", RegexOptions.Singleline)]
    private static partial Regex Comments();

    [GeneratedRegex(@"<\s*(br|/p|/li|/h\d|/div|/tr)\s*/?>", RegexOptions.IgnoreCase)]
    private static partial Regex LineBreaks();

    [GeneratedRegex(@"\n\s*\n+")]
    private static partial Regex BlankLines();

    public static string From(string html)
    {
        var s = Comments().Replace(html, " ");
        s = Hidden().Replace(s, " ");
        s = LineBreaks().Replace(s, "\n");
        s = RecipeJsonLd.Text(s, keepLines: true);
        return BlankLines().Replace(s, "\n").Trim();
    }
}
