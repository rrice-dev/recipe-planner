using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using RecipePlanner.Api.Contracts;
using RecipePlanner.Api.Data;
using RecipePlanner.Api.Library;
using RecipePlanner.Api.Models;

namespace RecipePlanner.Api.Endpoints;

public static class LibraryEndpoints
{
    public static void MapLibraryEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/library");

        // Browsing needs no login, so a shared link to the library works for anyone.
        // The whole collection is small, so the page filters it in the browser.
        group.MapGet("/", (RecipeLibrary library) =>
            Results.Ok(library.All.Select(r => new LibrarySummaryResponse(
                r.Slug, r.Title, r.Description, r.Cuisine, r.Country, r.Servings, r.PrepMinutes, r.CookMinutes,
                RecipeLibrary.TagsFor(r), r.Ingredients.Count))));

        group.MapGet("/{slug}", (string slug, RecipeLibrary library) =>
        {
            var r = library.Find(slug);
            if (r is null) return Results.NotFound();
            return Results.Ok(new LibraryDetailResponse(
                r.Slug, r.Title, r.Description, r.Cuisine, r.Country, r.Servings, r.PrepMinutes, r.CookMinutes,
                RecipeLibrary.TagsFor(r),
                r.Ingredients.Select(i => new LibraryIngredientResponse(
                    i.Name, i.Quantity, i.Unit, i.Note, library.SeedSections.GetValueOrDefault(i.Name))).ToList(),
                r.Steps.Select((s, n) => new LibraryStepResponse(n + 1, s.Instruction, s.Technique)).ToList()));
        });

        // Copies the recipe into the signed-in user's book. The copy is theirs: editing or
        // deleting it never touches the library, and a later library update never touches it.
        group.MapPost("/{slug}/add", async (string slug, ClaimsPrincipal user, RecipeLibrary library, AppDbContext db) =>
        {
            var source = library.Find(slug);
            if (source is null) return Results.NotFound();

            var recipe = await CopyAsync(db, library, source, user.UserId());
            await db.SaveChangesAsync();
            return Results.Created($"/api/recipes/{recipe.Id}", new LibraryAddResponse(recipe.Id));
        }).RequireAuthorization();
    }

    /// <summary>Builds the user's own copy of a library recipe and adds it to the context (the caller saves).</summary>
    public static async Task<Recipe> CopyAsync(AppDbContext db, RecipeLibrary library, LibraryRecipe source, string userId)
    {
        var ingredients = await FindOrCreateIngredientsAsync(db, library, source, userId);

        var techniqueNames = source.Steps.Where(s => s.Technique is not null)
            .Select(s => s.Technique!.ToLower()).Distinct().ToList();
        var techniques = (await db.Techniques
                .Where(t => techniqueNames.Contains(t.Name.ToLower()))
                .ToListAsync())
            .ToDictionary(t => t.Name, StringComparer.OrdinalIgnoreCase);

        var recipe = new Recipe
        {
            Title = source.Title,
            Description = source.Description,
            Cuisine = source.Cuisine,
            Servings = source.Servings,
            PrepMinutes = source.PrepMinutes,
            CookMinutes = source.CookMinutes,
            OwnerId = userId,
            LibrarySlug = source.Slug,
            Ingredients = source.Ingredients.Select(i => new RecipeIngredient
            {
                Ingredient = ingredients[i.Name],
                Quantity = i.Quantity,
                Unit = i.Unit,
                Note = i.Note
            }).ToList(),
            Steps = source.Steps.Select((s, n) => new Step
            {
                Order = n + 1,
                Instruction = s.Instruction,
                Technique = s.Technique is not null ? techniques.GetValueOrDefault(s.Technique) : null
            }).ToList(),
            // A technique the database doesn't know is left off rather than failing the copy
            Techniques = techniques.Values.ToList()
        };

        db.Recipes.Add(recipe);
        return recipe;
    }

    private static async Task<Dictionary<string, Ingredient>> FindOrCreateIngredientsAsync(
        AppDbContext db, RecipeLibrary library, LibraryRecipe source, string userId)
    {
        var wanted = source.Ingredients.Select(i => i.Name.ToLower()).Distinct().ToList();
        // The same name can exist twice in different case, like a shared "Garlic" and your own "GARLIC".
        // Shared ones come first, so the library recipe uses the shared spelling.
        var found = new Dictionary<string, Ingredient>(StringComparer.OrdinalIgnoreCase);
        var matches = await db.Ingredients.VisibleTo(userId).Where(i => wanted.Contains(i.Name.ToLower())).ToListAsync();
        foreach (var match in matches.OrderBy(i => i.OwnerId is null ? 0 : 1).ThenBy(i => i.Id))
            found.TryAdd(match.Name, match);
        // Ingredients created earlier in this same save (copying several recipes at once) aren't in
        // the database yet, so look in the context too, or the same name would be added twice
        foreach (var pending in db.Ingredients.Local.Where(i => i.OwnerId is null))
            found.TryAdd(pending.Name, pending);

        // Normally every name is already seeded; this covers a database that was seeded before
        // an ingredient was added to the seed file
        foreach (var i in source.Ingredients)
        {
            if (found.ContainsKey(i.Name)) continue;
            var created = new Ingredient { Name = i.Name, StoreSection = library.SeedSections.GetValueOrDefault(i.Name) };
            db.Ingredients.Add(created);
            found[i.Name] = created;
        }
        return found;
    }
}
