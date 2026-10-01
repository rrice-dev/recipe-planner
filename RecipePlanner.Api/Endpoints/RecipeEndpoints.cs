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
        {
            var recipe = await db.Recipes.AsNoTracking()
                .Include(r => r.Techniques)
                .Include(r => r.Ingredients).ThenInclude(ri => ri.Ingredient)
                .Include(r => r.Steps).ThenInclude(s => s.Technique)
                .AsSplitQuery()
                .FirstOrDefaultAsync(r => r.Id == id);
            return recipe is null ? Results.NotFound() : Results.Ok(ToDetail(recipe));
        });

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

        // Techniques
        group.MapPost("/{id:int}/techniques/{techniqueId:int}", async (int id, int techniqueId, AppDbContext db) =>
        {
            var recipe = await db.Recipes.Include(r => r.Techniques).FirstOrDefaultAsync(r => r.Id == id);
            if (recipe is null) return Results.NotFound();

            var technique = await db.Techniques.FindAsync(techniqueId);
            if (technique is null) return Results.NotFound();

            if (!recipe.Techniques.Any(t => t.Id == techniqueId))
            {
                recipe.Techniques.Add(technique);
                await db.SaveChangesAsync();
            }
            return Results.NoContent();
        });

        group.MapDelete("/{id:int}/techniques/{techniqueId:int}", async (int id, int techniqueId, AppDbContext db) =>
        {
            var recipe = await db.Recipes.Include(r => r.Techniques).FirstOrDefaultAsync(r => r.Id == id);
            if (recipe is null) return Results.NotFound();

            var technique = recipe.Techniques.FirstOrDefault(t => t.Id == techniqueId);
            if (technique is null) return Results.NotFound();

            recipe.Techniques.Remove(technique);
            await db.SaveChangesAsync();
            return Results.NoContent();
        });

        // Ingredients: replaces the recipe's full ingredient list
        group.MapPut("/{id:int}/ingredients", async (int id, List<RecipeIngredientRequest> items, AppDbContext db) =>
        {
            var recipe = await db.Recipes.Include(r => r.Ingredients).FirstOrDefaultAsync(r => r.Id == id);
            if (recipe is null) return Results.NotFound();

            var errors = new Dictionary<string, string[]>();
            if (items.Select(i => i.IngredientId).Distinct().Count() != items.Count)
                errors["Ingredients"] = ["Each ingredient can only appear once."];
            if (items.Any(i => i.Quantity <= 0))
                errors["Quantity"] = ["Quantities must be greater than zero."];
            if (items.Any(i => string.IsNullOrWhiteSpace(i.Unit)))
                errors["Unit"] = ["Unit is required."];
            if (errors.Count > 0) return Results.ValidationProblem(errors);

            var ids = items.Select(i => i.IngredientId).ToList();
            var existingCount = await db.Ingredients.CountAsync(i => ids.Contains(i.Id));
            if (existingCount != ids.Count)
                return Results.ValidationProblem(new Dictionary<string, string[]>
                    { ["IngredientId"] = ["One or more ingredients do not exist."] });

            var incoming = items.ToDictionary(i => i.IngredientId);
            foreach (var existing in recipe.Ingredients.ToList())
            {
                if (incoming.Remove(existing.IngredientId, out var req))
                {
                    existing.Quantity = req.Quantity;
                    existing.Unit = req.Unit.Trim();
                    existing.Note = req.Note;
                }
                else
                {
                    recipe.Ingredients.Remove(existing);
                }
            }
            foreach (var req in incoming.Values)
            {
                recipe.Ingredients.Add(new RecipeIngredient
                {
                    IngredientId = req.IngredientId,
                    Quantity = req.Quantity,
                    Unit = req.Unit.Trim(),
                    Note = req.Note
                });
            }

            await db.SaveChangesAsync();
            return Results.NoContent();
        });

        // Steps: replaces the recipe's full step list, in order
        group.MapPut("/{id:int}/steps", async (int id, List<StepRequest> steps, AppDbContext db) =>
        {
            var recipe = await db.Recipes.Include(r => r.Steps).FirstOrDefaultAsync(r => r.Id == id);
            if (recipe is null) return Results.NotFound();

            if (steps.Any(s => string.IsNullOrWhiteSpace(s.Instruction)))
                return Results.ValidationProblem(new Dictionary<string, string[]>
                    { ["Instruction"] = ["Every step needs an instruction."] });

            var techniqueIds = steps.Where(s => s.TechniqueId.HasValue)
                .Select(s => s.TechniqueId!.Value).Distinct().ToList();
            var foundTechniques = await db.Techniques.CountAsync(t => techniqueIds.Contains(t.Id));
            if (foundTechniques != techniqueIds.Count)
                return Results.ValidationProblem(new Dictionary<string, string[]>
                    { ["TechniqueId"] = ["One or more techniques do not exist."] });

            recipe.Steps.Clear();
            for (var i = 0; i < steps.Count; i++)
            {
                recipe.Steps.Add(new Step
                {
                    Order = i + 1,
                    Instruction = steps[i].Instruction.Trim(),
                    TechniqueId = steps[i].TechniqueId
                });
            }

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

    private static RecipeDetailResponse ToDetail(Recipe r) =>
        new(r.Id, r.Title, r.Description, r.Cuisine, r.Servings, r.PrepMinutes, r.CookMinutes,
            r.Techniques.OrderBy(t => t.Name)
                .Select(t => new TechniqueSummary(t.Id, t.Name, t.Difficulty))
                .ToList(),
            r.Ingredients.OrderBy(i => i.Ingredient.Name)
                .Select(i => new RecipeIngredientResponse(
                    i.IngredientId, i.Ingredient.Name, i.Quantity, i.Unit, i.Note, i.Ingredient.StoreSection))
                .ToList(),
            r.Steps.OrderBy(s => s.Order)
                .Select(s => new StepResponse(s.Order, s.Instruction, s.TechniqueId, s.Technique?.Name))
                .ToList());
}