using Microsoft.AspNetCore.Identity;

namespace RecipePlanner.Api.Models;

public class AppUser : IdentityUser
{
    // ---------- Settings (one row per user, so they follow you to every device) ----------

    /// <summary>"system", "light" or "dark"</summary>
    public string Theme { get; set; } = UserSettings.SystemTheme;

    /// <summary>null for no restriction, or "AIP" / "Vegetarian"</summary>
    public string? Diet { get; set; }

    /// <summary>Starting servings for new recipes and planned meals</summary>
    public int DefaultServings { get; set; } = UserSettings.DefaultServingsValue;

    /// <summary>"us" (cups, ounces, °F) or "metric" (ml, grams)</summary>
    public string Units { get; set; } = UserSettings.UsUnits;

    // Optional daily nutrition targets, for comparing a planned week. Null = no target.
    public int? CalorieTarget { get; set; }
    public int? ProteinTarget { get; set; }
    public int? CarbsTarget { get; set; }
    public int? FatTarget { get; set; }
}

public static class UserSettings
{
    public const string SystemTheme = "system";
    public static readonly string[] Themes = [SystemTheme, "light", "dark"];
    public static readonly string[] Diets = [Library.Diet.Aip, Library.Diet.Vegetarian];
    public const int DefaultServingsValue = 4;
    public const string UsUnits = "us";
    public static readonly string[] UnitSystems = [UsUnits, "metric"];
    public const int MaxServings = 100;
    public const int MinCalorieTarget = 500, MaxCalorieTarget = 10_000, MaxGramTarget = 1_000;
}
