using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using RecipePlanner.Api.Contracts;
using RecipePlanner.Api.Data;
using RecipePlanner.Api.Library;
using RecipePlanner.Api.Models;
using RecipePlanner.Api.Photos;

namespace RecipePlanner.Api.Endpoints;

public static class RecipeEndpoints
{
    public static void MapRecipeEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/recipes").RequireAuthorization();

        group.MapGet("/", async (ClaimsPrincipal user, AppDbContext db) =>
        {
            var userId = user.UserId();
            var recipes = await db.Recipes.AsNoTracking()
                .Where(r => r.OwnerId == userId)
                .Include(r => r.Ingredients).ThenInclude(ri => ri.Ingredient)
                .Include(r => r.CookLog)
                .Include(r => r.Collections)
                .AsSplitQuery()
                .OrderBy(r => r.Title)
                .ToListAsync();
            return Results.Ok(recipes.Select(ToListItem));
        });

        group.MapGet("/{id:int}", async (int id, ClaimsPrincipal user, AppDbContext db) =>
        {
            var userId = user.UserId();
            var recipe = await db.Recipes.AsNoTracking()
                .Include(r => r.Techniques)
                .Include(r => r.Ingredients).ThenInclude(ri => ri.Ingredient)
                .Include(r => r.Steps).ThenInclude(s => s.Technique)
                .Include(r => r.Ratings)
                .Include(r => r.CookLog)
                .Include(r => r.Collections)
                .Include(r => r.SourcePages)
                .AsSplitQuery()
                .FirstOrDefaultAsync(r => r.Id == id && r.OwnerId == userId);
            return recipe is null ? Results.NotFound() : Results.Ok(ToDetail(recipe) with
            {
                Personal = Personal(recipe),
                SourceUrl = recipe.SourceUrl,
                SourcePages = SourcePages.ToResponse(recipe.SourcePages)
            });
        });

        group.MapPost("/", async (RecipeRequest request, ClaimsPrincipal user, AppDbContext db) =>
        {
            var errors = Validate(request);
            if (errors.Count > 0) return Results.ValidationProblem(errors);

            var recipe = new Recipe { Title = request.Title.Trim(), OwnerId = user.UserId() };
            Apply(recipe, request);
            db.Recipes.Add(recipe);
            await db.SaveChangesAsync();

            return Results.Created($"/api/recipes/{recipe.Id}", ToResponse(recipe));
        });

        group.MapPut("/{id:int}", async (int id, RecipeRequest request, ClaimsPrincipal user, AppDbContext db) =>
        {
            var errors = Validate(request);
            if (errors.Count > 0) return Results.ValidationProblem(errors);

            var userId = user.UserId();
            var recipe = await db.Recipes.FirstOrDefaultAsync(r => r.Id == id && r.OwnerId == userId);
            if (recipe is null) return Results.NotFound();

            Apply(recipe, request);
            await db.SaveChangesAsync();
            return Results.NoContent();
        });

        group.MapDelete("/{id:int}", async (int id, ClaimsPrincipal user, AppDbContext db, IPhotoStore photos) =>
        {
            var userId = user.UserId();
            var recipe = await db.Recipes.Include(r => r.SourcePages).FirstOrDefaultAsync(r => r.Id == id && r.OwnerId == userId);
            if (recipe is null) return Results.NotFound();

            // Photos live outside the database, so remove them once the recipe is gone
            var files = recipe.SourcePages.Select(p => p.PhotoName).Append(recipe.PhotoName).OfType<string>().ToList();
            db.Recipes.Remove(recipe);
            await db.SaveChangesAsync();
            foreach (var file in files) await photos.DeleteAsync(file, CancellationToken.None);
            return Results.NoContent();
        });

        // Techniques
        group.MapPost("/{id:int}/techniques/{techniqueId:int}",
            async (int id, int techniqueId, ClaimsPrincipal user, AppDbContext db) =>
        {
            var userId = user.UserId();
            var recipe = await db.Recipes.Include(r => r.Techniques)
                .FirstOrDefaultAsync(r => r.Id == id && r.OwnerId == userId);
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

        group.MapDelete("/{id:int}/techniques/{techniqueId:int}",
            async (int id, int techniqueId, ClaimsPrincipal user, AppDbContext db) =>
        {
            var userId = user.UserId();
            var recipe = await db.Recipes.Include(r => r.Techniques)
                .FirstOrDefaultAsync(r => r.Id == id && r.OwnerId == userId);
            if (recipe is null) return Results.NotFound();

            var technique = recipe.Techniques.FirstOrDefault(t => t.Id == techniqueId);
            if (technique is null) return Results.NotFound();

            recipe.Techniques.Remove(technique);
            await db.SaveChangesAsync();
            return Results.NoContent();
        });

        // Ingredients: replaces the recipe's full ingredient list
        group.MapPut("/{id:int}/ingredients",
            async (int id, List<RecipeIngredientRequest> items, ClaimsPrincipal user, AppDbContext db) =>
        {
            var userId = user.UserId();
            var recipe = await db.Recipes.Include(r => r.Ingredients)
                .FirstOrDefaultAsync(r => r.Id == id && r.OwnerId == userId);
            if (recipe is null) return Results.NotFound();

            var errors = new Dictionary<string, string[]>();
            if (items.Count > RecipeLimits.MaxIngredients)
                errors["Ingredients"] = [$"A recipe can have up to {RecipeLimits.MaxIngredients} ingredients."];
            if (items.Select(i => i.IngredientId).Distinct().Count() != items.Count)
                errors["Ingredients"] = ["Each ingredient can only appear once."];
            if (items.Any(i => i.Quantity is < RecipeLimits.MinQuantity or > RecipeLimits.MaxQuantity))
                errors["Quantity"] = [$"Quantities must be between {RecipeLimits.MinQuantity} and {RecipeLimits.MaxQuantity:N0}."];
            if (items.Any(i => string.IsNullOrWhiteSpace(i.Unit)))
                errors["Unit"] = ["Unit is required."];
            else if (items.Any(i => i.Unit.Trim().Length > RecipeLimits.MaxUnitLength))
                errors["Unit"] = [$"Units must be {RecipeLimits.MaxUnitLength} characters or fewer."];
            if (items.Any(i => i.Note?.Length > RecipeLimits.MaxIngredientNoteLength))
                errors["Note"] = [$"Notes must be {RecipeLimits.MaxIngredientNoteLength} characters or fewer."];
            if (errors.Count > 0) return Results.ValidationProblem(errors);

            var ids = items.Select(i => i.IngredientId).ToList();
            // Shared ones or your own: someone else's private ingredient doesn't exist as far as you're concerned
            var existingCount = await db.Ingredients.VisibleTo(userId).CountAsync(i => ids.Contains(i.Id));
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
        group.MapPut("/{id:int}/steps",
            async (int id, List<StepRequest> steps, ClaimsPrincipal user, AppDbContext db) =>
        {
            var userId = user.UserId();
            var recipe = await db.Recipes.Include(r => r.Steps)
                .FirstOrDefaultAsync(r => r.Id == id && r.OwnerId == userId);
            if (recipe is null) return Results.NotFound();

            if (steps.Count > RecipeLimits.MaxSteps)
                return Results.ValidationProblem(new Dictionary<string, string[]>
                    { ["Steps"] = [$"A recipe can have up to {RecipeLimits.MaxSteps} steps."] });
            if (steps.Any(s => string.IsNullOrWhiteSpace(s.Instruction)))
                return Results.ValidationProblem(new Dictionary<string, string[]>
                    { ["Instruction"] = ["Every step needs an instruction."] });
            if (steps.Any(s => s.Instruction.Trim().Length > RecipeLimits.MaxStepLength))
                return Results.ValidationProblem(new Dictionary<string, string[]>
                    { ["Instruction"] = [$"Each step must be {RecipeLimits.MaxStepLength} characters or fewer."] });

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
        // Only set by imports; editing a recipe later (which doesn't send it) keeps the original source
        if (!string.IsNullOrWhiteSpace(request.SourceUrl)) recipe.SourceUrl = request.SourceUrl.Trim();
        if (request.FromCookbook is { } fromCookbook)
        {
            recipe.FromCookbook = fromCookbook;
            if (fromCookbook) { recipe.ShareToken = null; recipe.SharedAt = null; }
        }
    }

    private static Dictionary<string, string[]> Validate(RecipeRequest r)
    {
        var errors = new Dictionary<string, string[]>();
        if (string.IsNullOrWhiteSpace(r.Title))
            errors["Title"] = ["Title is required."];
        else if (r.Title.Length > 200)
            errors["Title"] = ["Title must be 200 characters or fewer."];
        if (r.Description?.Length > RecipeLimits.MaxDescriptionLength)
            errors["Description"] = [$"Description must be {RecipeLimits.MaxDescriptionLength} characters or fewer."];
        if (r.Cuisine?.Length > RecipeLimits.MaxCuisineLength)
            errors["Cuisine"] = [$"Cuisine must be {RecipeLimits.MaxCuisineLength} characters or fewer."];
        if (r.Servings < 1)
            errors["Servings"] = ["Servings must be at least 1."];
        if (r.PrepMinutes < 0 || r.CookMinutes < 0)
            errors["Time"] = ["Times cannot be negative."];
        // Shown as a link, so only ever an ordinary web address (never javascript: or data:)
        if (!string.IsNullOrWhiteSpace(r.SourceUrl)
            && (r.SourceUrl.Length > RecipeLimits.MaxSourceUrlLength
                || !Uri.TryCreate(r.SourceUrl.Trim(), UriKind.Absolute, out var source)
                || source.Scheme is not ("https" or "http")))
            errors["SourceUrl"] = ["Source must be a web address starting with https:// or http://."];
        return errors;
    }

    private static RecipeResponse ToResponse(Recipe r) =>
        new(r.Id, r.Title, r.Description, r.Cuisine, r.Servings, r.PrepMinutes, r.CookMinutes, RecipePhotos.Version(r));

    private static RecipeResponse ToListItem(Recipe r)
    {
        var ingredients = r.Ingredients.Select(i => (i.Ingredient.Name, i.Ingredient.StoreSection)).ToList();
        var diets = ingredients.Count == 0 ? [] : new[] { Diet.Aip, Diet.Vegetarian }.Where(d => Diet.Fits(d, ingredients)).ToList();
        return ToResponse(r) with
        {
            IsFavorite = r.IsFavorite,
            TimesCooked = r.CookLog.Count,
            LastCooked = r.CookLog.Count == 0 ? null : r.CookLog.Max(c => c.CookedOn),
            CollectionIds = r.Collections.Select(c => c.Id).Order().ToList(),
            IngredientNames = ingredients.Select(i => i.Name).Order().ToList(),
            DietTags = diets
        };
    }

    private static RecipePersonalResponse Personal(Recipe r) => new(
        r.IsFavorite, r.Notes,
        r.CookLog.OrderByDescending(c => c.CookedOn).ThenByDescending(c => c.Id).Select(c => new CookLogResponse(c.Id, c.CookedOn)).ToList(),
        r.Collections.Select(c => c.Id).Order().ToList());

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
                .ToList(),
            Social(r),
            RecipePhotos.Version(r));

    private static RecipeSocialResponse Social(Recipe r)
    {
        var (average, count) = Ratings.Community(r.Ratings, r.OwnerId);
        return new RecipeSocialResponse(
            r.ShareToken,
            r.Ratings.FirstOrDefault(x => x.UserId == r.OwnerId)?.Stars,
            average,
            count,
            r.FromCookbook);
    }
}