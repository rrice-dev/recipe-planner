using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using RecipePlanner.Api.Models;

namespace RecipePlanner.Api.Data;

public static class DbSeeder
{
    private static readonly JsonSerializerOptions Options = new() { PropertyNameCaseInsensitive = true };

    public static async Task SeedAsync(AppDbContext db, string contentRoot)
    {
        var seedDir = Path.Combine(contentRoot, "Data", "Seed");

        var techniques = await Load<List<TechniqueSeed>>(Path.Combine(seedDir, "techniques.json"));
        var techniqueNames = new HashSet<string>(
            await db.Techniques.Select(t => t.Name).ToListAsync(), StringComparer.OrdinalIgnoreCase);
        foreach (var t in techniques)
        {
            var name = t.Name.Trim();
            if (!techniqueNames.Add(name)) continue; // already in DB or earlier in the file
            db.Techniques.Add(new Technique
            {
                Name = name,
                Summary = t.Summary,
                DonenessCues = t.DonenessCues,
                CommonMistakes = t.CommonMistakes,
                Difficulty = t.Difficulty
            });
        }

        var ingredients = await Load<List<IngredientSeed>>(Path.Combine(seedDir, "ingredients.json"));
        var ingredientNames = new HashSet<string>(
            await db.Ingredients.Select(i => i.Name).ToListAsync(), StringComparer.OrdinalIgnoreCase);
        foreach (var i in ingredients)
        {
            var name = i.Name.Trim();
            if (!ingredientNames.Add(name)) continue;
            db.Ingredients.Add(new Ingredient { Name = name, StoreSection = i.StoreSection });
        }

        await db.SaveChangesAsync();
    }

    private static async Task<T> Load<T>(string path)
    {
        await using var stream = File.OpenRead(path);
        return (await JsonSerializer.DeserializeAsync<T>(stream, Options))!;
    }

    private record TechniqueSeed(string Name, string Summary, string? DonenessCues, string? CommonMistakes, int Difficulty);
    private record IngredientSeed(string Name, string? StoreSection);
}