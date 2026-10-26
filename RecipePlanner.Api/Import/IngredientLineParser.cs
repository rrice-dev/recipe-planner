using System.Globalization;
using System.Net;
using System.Text.RegularExpressions;

namespace RecipePlanner.Api.Import;

public record ParsedIngredient(string Name, decimal Quantity, string Unit, string? Note);

// Turns a line like "1 ½ cups all-purpose flour, sifted" into 1.5 / cup / All-purpose flour / sifted.
// Recipe sites write these by hand, so this aims for "right most of the time" and keeps anything it
// can't place in the note rather than dropping it. Imports are always shown for review before saving.
public static partial class IngredientLineParser
{
    private static readonly Dictionary<char, decimal> UnicodeFractions = new()
    {
        ['½'] = 0.5m, ['⅓'] = 1m / 3, ['⅔'] = 2m / 3, ['¼'] = 0.25m, ['¾'] = 0.75m, ['⅕'] = 0.2m,
        ['⅛'] = 0.125m, ['⅜'] = 0.375m, ['⅝'] = 0.625m, ['⅞'] = 0.875m, ['⅙'] = 1m / 6,
    };

    // Spellings people use -> the units the app uses
    private static readonly Dictionary<string, string> Units = new(StringComparer.OrdinalIgnoreCase)
    {
        ["cup"] = "cup", ["cups"] = "cup", ["c"] = "cup", ["c."] = "cup",
        ["tablespoon"] = "tbsp", ["tablespoons"] = "tbsp", ["tbsp"] = "tbsp", ["tbsp."] = "tbsp", ["tbs"] = "tbsp", ["tbl"] = "tbsp", ["T"] = "tbsp",
        ["teaspoon"] = "tsp", ["teaspoons"] = "tsp", ["tsp"] = "tsp", ["tsp."] = "tsp", ["t"] = "tsp",
        ["ounce"] = "oz", ["ounces"] = "oz", ["oz"] = "oz", ["oz."] = "oz",
        ["fluid ounce"] = "fl_oz", ["fluid ounces"] = "fl_oz", ["fl oz"] = "fl_oz", ["fl. oz."] = "fl_oz",
        ["pound"] = "lb", ["pounds"] = "lb", ["lb"] = "lb", ["lbs"] = "lb", ["lb."] = "lb", ["lbs."] = "lb",
        ["gram"] = "g", ["grams"] = "g", ["g"] = "g", ["kilogram"] = "kg", ["kilograms"] = "kg", ["kg"] = "kg",
        ["milliliter"] = "ml", ["milliliters"] = "ml", ["millilitre"] = "ml", ["millilitres"] = "ml", ["ml"] = "ml",
        ["liter"] = "l", ["liters"] = "l", ["litre"] = "l", ["litres"] = "l", ["l"] = "l",
        ["clove"] = "clove", ["cloves"] = "clove", ["can"] = "can", ["cans"] = "can", ["slice"] = "slice", ["slices"] = "slice",
        ["pinch"] = "pinch", ["pinches"] = "pinch", ["dash"] = "dash", ["dashes"] = "dash", ["stalk"] = "stalk", ["stalks"] = "stalk",
        ["sprig"] = "sprig", ["sprigs"] = "sprig", ["head"] = "head", ["heads"] = "head", ["bunch"] = "bunch", ["bunches"] = "bunch",
        ["package"] = "package", ["packages"] = "package", ["pkg"] = "package", ["stick"] = "stick", ["sticks"] = "stick",
        ["inch"] = "inch", ["inches"] = "inch", ["piece"] = "piece", ["pieces"] = "piece", ["jar"] = "jar", ["jars"] = "jar",
        ["bag"] = "bag", ["bags"] = "bag", ["handful"] = "handful", ["large"] = "whole", ["medium"] = "whole", ["small"] = "whole",
    };

    // "1", "1.5", "1 1/2", "1/2", "1½", "½", ranges like "2-3" or "2 to 3"
    [GeneratedRegex(@"^\s*(?<q>(\d+\s+\d+/\d+)|(\d+/\d+)|(\d*[½⅓⅔¼¾⅕⅛⅜⅝⅞⅙])|(\d+(\.\d+)?))(\s*(-|–|to)\s*(?<q2>(\d+\s+\d+/\d+)|(\d+/\d+)|(\d*[½⅓⅔¼¾⅕⅛⅜⅝⅞⅙])|(\d+(\.\d+)?)))?\s*(?<rest>.*)$")]
    private static partial Regex Leading();

    [GeneratedRegex(@"\(([^)]*)\)")]
    private static partial Regex Parenthetical();

    [GeneratedRegex(@"\s+")]
    private static partial Regex Spaces();

    public static ParsedIngredient Parse(string line)
    {
        var text = Spaces().Replace(WebUtility.HtmlDecode(line ?? "").Replace(' ', ' '), " ").Trim();
        var m = Leading().Match(text);
        decimal quantity;
        string rest;
        if (m.Success && TryNumber(m.Groups["q"].Value, out var q) && q > 0)
        {
            // For a range, use the larger amount: it's what you'd buy
            quantity = m.Groups["q2"].Success && TryNumber(m.Groups["q2"].Value, out var q2) && q2 > q ? q2 : q;
            rest = m.Groups["rest"].Value.Trim();
        }
        else
        {
            // "Salt and pepper to taste": no amount given
            return Finish(text, 1, "whole", "amount not given");
        }

        // "(14 oz) can tomatoes": a size in parentheses before the unit becomes part of the note
        string? sizeNote = null;
        if (rest.StartsWith('('))
        {
            var close = rest.IndexOf(')');
            if (close > 0) { sizeNote = rest[1..close].Trim(); rest = rest[(close + 1)..].Trim(); }
        }

        var unit = "whole";
        foreach (var candidate in new[] { 3, 2, 1 })
        {
            var words = rest.Split(' ', 4);
            if (words.Length < candidate) continue;
            var key = string.Join(' ', words.Take(candidate));
            if (Units.TryGetValue(key, out var u) || Units.TryGetValue(key.TrimEnd('.', ','), out u))
            {
                // A lone "t" or "c" is only a unit when it's clearly abbreviated (followed by more words)
                if (candidate == 1 && key.Length == 1 && words.Length < 2) break;
                unit = u;
                rest = string.Join(' ', words.Skip(candidate)).Trim();
                if (rest.StartsWith("of ", StringComparison.OrdinalIgnoreCase)) rest = rest[3..];
                break;
            }
        }

        return Finish(rest, quantity, unit, sizeNote);
    }

    private static ParsedIngredient Finish(string rest, decimal quantity, string unit, string? extraNote)
    {
        var notes = new List<string>();
        if (extraNote is not null) notes.Add(extraNote);
        // Notes in parentheses or after the first comma: "(about 3)", "finely chopped"
        foreach (Match p in Parenthetical().Matches(rest)) notes.Add(p.Groups[1].Value.Trim());
        var name = Parenthetical().Replace(rest, "").Trim();
        var comma = name.IndexOf(',');
        if (comma > 0)
        {
            notes.Add(name[(comma + 1)..].Trim());
            name = name[..comma].Trim();
        }
        name = name.Trim(' ', '-', '–', '*', '•');
        if (name.Length == 0) name = "Ingredient";
        if (name.Length > 100) name = name[..100].TrimEnd();
        name = char.ToUpperInvariant(name[0]) + name[1..];

        var note = string.Join("; ", notes.Where(n => n.Length > 0));
        if (note.Length > 80) note = note[..79].TrimEnd() + "…";
        return new ParsedIngredient(name, Math.Round(quantity, 3), unit, note.Length == 0 ? null : note);
    }

    private static bool TryNumber(string s, out decimal value)
    {
        value = 0;
        s = s.Trim();
        if (s.Length == 0) return false;
        // "1½"
        var last = s[^1];
        if (UnicodeFractions.TryGetValue(last, out var frac))
        {
            var whole = s[..^1].Trim();
            value = frac + (whole.Length > 0 && decimal.TryParse(whole, NumberStyles.Number, CultureInfo.InvariantCulture, out var w) ? w : 0);
            return true;
        }
        // "1 1/2"
        var parts = s.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        decimal total = 0;
        foreach (var part in parts)
        {
            if (part.Contains('/'))
            {
                var fr = part.Split('/');
                if (fr.Length != 2 || !decimal.TryParse(fr[0], NumberStyles.Number, CultureInfo.InvariantCulture, out var n)
                    || !decimal.TryParse(fr[1], NumberStyles.Number, CultureInfo.InvariantCulture, out var d) || d == 0) return false;
                total += n / d;
            }
            else if (decimal.TryParse(part, NumberStyles.Number, CultureInfo.InvariantCulture, out var n)) total += n;
            else return false;
        }
        value = total;
        return true;
    }
}
