using Microsoft.EntityFrameworkCore;
using RecipePlanner.Api.Contracts;
using RecipePlanner.Api.Data;
using RecipePlanner.Api.Models;

namespace RecipePlanner.Api.Endpoints;

public static class RecipeEndpoints
{
    public static void MapRecipeEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/recipes");

        group.MapGet("/", async (AppDbContext db) =>
        {
            var recipes = await db.Recipes.AsNoTracking().OrderBy(r => r.Title).ToListAsync();
            return Results.Ok(recipes.Select(ToResponse));
        });

        group.MapGet("/{id:int}", async (int id, AppDbContext db) =>
            await db.Recipes.FindAsync(id) is { } recipe
                ? Results.Ok(ToResponse(recipe))
                : Results.NotFound());

        group.MapPost("/", async (RecipeRequest request, AppDbContext db) =>
        {
            var errors = Validate(request);
            if (errors.Count > 0) return Results.ValidationProblem(errors);

            var recipe = new Recipe { Title = request.Title.Trim() };
            Apply(recipe, request);
            db.Recipes.Add(recipe);
            await db.SaveChangesAsync();

            return Results.Created($"/api/recipes/{recipe.Id}", ToResponse(recipe));
        });

        group.MapPut("/{id:int}", async (int id, RecipeRequest request, AppDbContext db) =>
        {
            var errors = Validate(request);
            if (errors.Count > 0) return Results.ValidationProblem(errors);

            var recipe = await db.Recipes.FindAsync(id);
            if (recipe is null) return Results.NotFound();

            Apply(recipe, request);
            await db.SaveChangesAsync();
            return Results.NoContent();
        });

        group.MapDelete("/{id:int}", async (int id, AppDbContext db) =>
        {
            var recipe = await db.Recipes.FindAsync(id);
            if (recipe is null) return Results.NotFound();

            db.Recipes.Remove(recipe);
            await db.SaveChangesAsync();
            return Results.NoContent();
        });
    }

    private static void Apply(Recipe recipe, RecipeRequest request)
    {
        recipe.Title = request.Title.Trim();
        recipe.Description = request.Description;
        recipe.Cuisine = request.Cuisine;
        recipe.Servings = request.Servings;
        recipe.PrepMinutes = request.PrepMinutes;
        recipe.CookMinutes = request.CookMinutes;
    }

    private static Dictionary<string, string[]> Validate(RecipeRequest r)
    {
        var errors = new Dictionary<string, string[]>();
        if (string.IsNullOrWhiteSpace(r.Title))
            errors["Title"] = ["Title is required."];
        else if (r.Title.Length > 200)
            errors["Title"] = ["Title must be 200 characters or fewer."];
        if (r.Servings < 1)
            errors["Servings"] = ["Servings must be at least 1."];
        if (r.PrepMinutes < 0 || r.CookMinutes < 0)
            errors["Time"] = ["Times cannot be negative."];
        return errors;
    }

    private static RecipeResponse ToResponse(Recipe r) =>
        new(r.Id, r.Title, r.Description, r.Cuisine, r.Servings, r.PrepMinutes, r.CookMinutes);
}