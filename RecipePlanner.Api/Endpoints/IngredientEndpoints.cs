using Microsoft.EntityFrameworkCore;
using RecipePlanner.Api.Contracts;
using RecipePlanner.Api.Data;
using RecipePlanner.Api.Models;

namespace RecipePlanner.Api.Endpoints;

public static class IngredientEndpoints
{
    public static void MapIngredientEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/ingredients");

        group.MapGet("/", async (AppDbContext db) =>
        {
            var ingredients = await db.Ingredients.AsNoTracking().OrderBy(i => i.Name).ToListAsync();
            return Results.Ok(ingredients.Select(ToResponse));
        });

        group.MapGet("/{id:int}", async (int id, AppDbContext db) =>
            await db.Ingredients.FindAsync(id) is { } i
                ? Results.Ok(ToResponse(i))
                : Results.NotFound());

        group.MapPost("/", async (IngredientRequest request, AppDbContext db) =>
        {
            var errors = Validate(request);
            if (errors.Count > 0) return Results.ValidationProblem(errors);

            var name = request.Name.Trim();
            if (await db.Ingredients.AnyAsync(i => i.Name == name))
                return Results.Conflict(new { message = $"An ingredient named '{name}' already exists." });

            var ingredient = new Ingredient { Name = name, StoreSection = request.StoreSection };
            db.Ingredients.Add(ingredient);
            await db.SaveChangesAsync();

            return Results.Created($"/api/ingredients/{ingredient.Id}", ToResponse(ingredient));
        });

        group.MapPut("/{id:int}", async (int id, IngredientRequest request, AppDbContext db) =>
        {
            var errors = Validate(request);
            if (errors.Count > 0) return Results.ValidationProblem(errors);

            var ingredient = await db.Ingredients.FindAsync(id);
            if (ingredient is null) return Results.NotFound();

            var name = request.Name.Trim();
            if (await db.Ingredients.AnyAsync(i => i.Name == name && i.Id != id))
                return Results.Conflict(new { message = $"An ingredient named '{name}' already exists." });

            ingredient.Name = name;
            ingredient.StoreSection = request.StoreSection;
            await db.SaveChangesAsync();
            return Results.NoContent();
        });

        group.MapDelete("/{id:int}", async (int id, AppDbContext db) =>
        {
            var ingredient = await db.Ingredients.FindAsync(id);
            if (ingredient is null) return Results.NotFound();

            if (await db.Set<RecipeIngredient>().AnyAsync(ri => ri.IngredientId == id))
                return Results.Conflict(new { message = "This ingredient is used by one or more recipes." });

            db.Ingredients.Remove(ingredient);
            await db.SaveChangesAsync();
            return Results.NoContent();
        });
    }

    private static Dictionary<string, string[]> Validate(IngredientRequest r)
    {
        var errors = new Dictionary<string, string[]>();
        if (string.IsNullOrWhiteSpace(r.Name))
            errors["Name"] = ["Name is required."];
        else if (r.Name.Length > 100)
            errors["Name"] = ["Name must be 100 characters or fewer."];
        return errors;
    }

    private static IngredientResponse ToResponse(Ingredient i) => new(i.Id, i.Name, i.StoreSection);
}