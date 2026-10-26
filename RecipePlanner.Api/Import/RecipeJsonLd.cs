using System.Net;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Xml;

namespace RecipePlanner.Api.Import;

public record ScrapedRecipe(
    string Title, string? Description, string? Cuisine, int? Servings, int? PrepMinutes, int? CookMinutes,
    List<string> IngredientLines, List<string> Steps);

// Most recipe sites embed a machine-readable copy of the recipe for search engines
// (schema.org "Recipe" in a <script type="application/ld+json"> tag). Reading that is far more
// reliable than guessing from the page layout.
public static partial class RecipeJsonLd
{
    [GeneratedRegex(@"<script[^>]*type\s*=\s*[""']application/ld\+json[""'][^>]*>(?<json>.*?)</script>", RegexOptions.IgnoreCase | RegexOptions.Singleline)]
    private static partial Regex JsonLdScripts();

    [GeneratedRegex(@"<[^>]+>")]
    private static partial Regex Tags();

    [GeneratedRegex(@"\s+")]
    private static partial Regex Spaces();

    [GeneratedRegex(@"\d+")]
    private static partial Regex Digits();

    public static ScrapedRecipe? Find(string html)
    {
        foreach (Match m in JsonLdScripts().Matches(html))
        {
            try
            {
                using var doc = JsonDocument.Parse(m.Groups["json"].Value, new JsonDocumentOptions { AllowTrailingCommas = true, CommentHandling = JsonCommentHandling.Skip });
                if (FindRecipe(doc.RootElement, 0) is { } recipe && Read(recipe) is { } parsed) return parsed;
            }
            catch (JsonException)
            {
                // Broken JSON on one tag doesn't stop us checking the next
            }
        }
        return null;
    }

    // The Recipe can be the root, in an array, or inside "@graph", sometimes nested
    private static JsonElement? FindRecipe(JsonElement e, int depth)
    {
        if (depth > 6) return null;
        if (e.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in e.EnumerateArray())
                if (FindRecipe(item, depth + 1) is { } found) return found;
            return null;
        }
        if (e.ValueKind != JsonValueKind.Object) return null;
        if (e.TryGetProperty("@type", out var type) && IsRecipeType(type)) return e;
        if (e.TryGetProperty("@graph", out var graph)) return FindRecipe(graph, depth + 1);
        if (e.TryGetProperty("mainEntity", out var main)) return FindRecipe(main, depth + 1);
        return null;
    }

    private static bool IsRecipeType(JsonElement type) => type.ValueKind switch
    {
        JsonValueKind.String => type.GetString() is "Recipe" or "schema:Recipe" or "http://schema.org/Recipe" or "https://schema.org/Recipe",
        JsonValueKind.Array => type.EnumerateArray().Any(IsRecipeType),
        _ => false
    };

    private static ScrapedRecipe? Read(JsonElement r)
    {
        var title = Text(Str(r, "name"));
        if (string.IsNullOrWhiteSpace(title)) return null;
        var ingredients = Strings(r, "recipeIngredient").Concat(Strings(r, "ingredients")).Select(s => Text(s)).Where(s => s.Length > 0).ToList();
        var steps = new List<string>();
        if (r.TryGetProperty("recipeInstructions", out var instructions)) CollectSteps(instructions, steps, 0);

        var prep = Minutes(Str(r, "prepTime"));
        var cook = Minutes(Str(r, "cookTime"));
        var total = Minutes(Str(r, "totalTime"));
        if (cook is null && total is not null) cook = Math.Max(0, total.Value - (prep ?? 0));

        return new ScrapedRecipe(
            title,
            NullIfEmpty(Text(Str(r, "description"))),
            NullIfEmpty(Text(Strings(r, "recipeCuisine").FirstOrDefault() ?? "")),
            Servings(r),
            prep, cook, ingredients, steps.Where(s => s.Length > 0).ToList());
    }

    // Instructions come as one string, a list of strings, HowToStep objects, or HowToSections of steps
    private static void CollectSteps(JsonElement e, List<string> steps, int depth)
    {
        if (depth > 5) return;
        switch (e.ValueKind)
        {
            case JsonValueKind.String:
                // One big string: split on line breaks
                foreach (var line in Regex.Split(Text(e.GetString() ?? "", keepLines: true), @"\n+"))
                    if (line.Trim().Length > 0) steps.Add(line.Trim());
                break;
            case JsonValueKind.Array:
                foreach (var item in e.EnumerateArray()) CollectSteps(item, steps, depth + 1);
                break;
            case JsonValueKind.Object:
                if (e.TryGetProperty("itemListElement", out var items)) CollectSteps(items, steps, depth + 1);
                else if (Str(e, "text") is { Length: > 0 } text) steps.Add(Text(text));
                else if (Str(e, "name") is { Length: > 0 } name) steps.Add(Text(name));
                break;
        }
    }

    private static int? Servings(JsonElement r)
    {
        if (!r.TryGetProperty("recipeYield", out var y)) return null;
        var values = y.ValueKind == JsonValueKind.Array ? y.EnumerateArray().ToList() : [y];
        foreach (var v in values)
        {
            if (v.ValueKind == JsonValueKind.Number && v.TryGetInt32(out var n) && n > 0) return Math.Min(n, 100);
            if (v.ValueKind == JsonValueKind.String && Digits().Match(v.GetString() ?? "") is { Success: true } d
                && int.TryParse(d.Value, out var s) && s > 0) return Math.Min(s, 100);
        }
        return null;
    }

    /// <summary>ISO 8601 durations like "PT1H30M" -> 90</summary>
    public static int? Minutes(string? iso)
    {
        if (string.IsNullOrWhiteSpace(iso)) return null;
        try
        {
            var span = XmlConvert.ToTimeSpan(iso.Trim());
            var minutes = (int)Math.Round(span.TotalMinutes);
            return minutes is >= 0 and <= 7 * 24 * 60 ? minutes : null;
        }
        catch (FormatException)
        {
            return null;
        }
    }

    private static string? Str(JsonElement e, string name) =>
        e.TryGetProperty(name, out var v) ? v.ValueKind switch
        {
            JsonValueKind.String => v.GetString(),
            JsonValueKind.Number => v.GetRawText(),
            JsonValueKind.Array => v.EnumerateArray().Where(x => x.ValueKind == JsonValueKind.String).Select(x => x.GetString()).FirstOrDefault(),
            _ => null
        } : null;

    private static IEnumerable<string> Strings(JsonElement e, string name)
    {
        if (!e.TryGetProperty(name, out var v)) yield break;
        if (v.ValueKind == JsonValueKind.String) yield return v.GetString() ?? "";
        else if (v.ValueKind == JsonValueKind.Array)
            foreach (var x in v.EnumerateArray())
                if (x.ValueKind == JsonValueKind.String) yield return x.GetString() ?? "";
    }

    /// <summary>Plain text: entities decoded, tags removed, whitespace tidied</summary>
    public static string Text(string? s, bool keepLines = false)
    {
        if (string.IsNullOrEmpty(s)) return "";
        var decoded = WebUtility.HtmlDecode(WebUtility.HtmlDecode(s)); // some sites double-encode
        if (keepLines) decoded = Regex.Replace(decoded, @"<\s*(br|/p|/li)\s*/?>", "\n", RegexOptions.IgnoreCase);
        var noTags = Tags().Replace(decoded, " ");
        return keepLines
            ? string.Join('\n', noTags.Split('\n').Select(l => Spaces().Replace(l, " ").Trim()))
            : Spaces().Replace(noTags, " ").Trim();
    }

    private static string? NullIfEmpty(string s) => s.Length == 0 ? null : s;
}
