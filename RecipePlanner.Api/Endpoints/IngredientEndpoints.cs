using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using RecipePlanner.Api.Contracts;
using RecipePlanner.Api.Data;
using RecipePlanner.Api.Models;

namespace RecipePlanner.Api.Endpoints;

// The shared list (seeded, copied in from the library, or added by an admin) is visible to everyone.
// Ingredients a user adds are theirs alone, so nothing one person types shows up for anyone else.
public static class IngredientEndpoints
{
    // Plenty for a big recipe book, and stops one account from filling the table
    public const int MaxPerUser = 500;

    /// <summary>The shared ingredients plus this user's own (only shared when signed out)</summary>
    public static IQueryable<Ingredient> VisibleTo(this IQueryable<Ingredient> ingredients, string? userId) =>
        ingredients.Where(i => i.OwnerId == null || i.OwnerId == userId);

    private static string? SignedIn(ClaimsPrincipal user) =>
        user.Identity?.IsAuthenticated == true ? user.UserId() : null;

    public static void MapIngredientEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/ingredients");

        group.MapGet("/", async (ClaimsPrincipal user, AppDbContext db) =>
        {
            var ingredients = await db.Ingredients.AsNoTracking().VisibleTo(SignedIn(user)).OrderBy(i => i.Name).ToListAsync();
            return Results.Ok(ingredients.Select(ToResponse));
        });

        group.MapGet("/{id:int}", async (int id, ClaimsPrincipal user, AppDbContext db) =>
            await db.Ingredients.AsNoTracking().VisibleTo(SignedIn(user)).FirstOrDefaultAsync(i => i.Id == id) is { } i
                ? Results.Ok(ToResponse(i))
                : Results.NotFound());

        // An admin adds to the shared list; anyone else adds to their own
        group.MapPost("/", async (IngredientRequest request, ClaimsPrincipal user, AppDbContext db) =>
        {
            var errors = Validate(request);
            if (errors.Count > 0) return Results.ValidationProblem(errors);

            var userId = user.UserId();
            var owner = user.IsInRole(Roles.Admin) ? null : userId;
            var name = request.Name.Trim();
            // Already in the shared list or your own (someone else's private one doesn't count)
            if (await db.Ingredients.VisibleTo(userId).AnyAsync(i => i.Name == name))
                return Results.Conflict(new { message = $"An ingredient named '{name}' already exists." });
            if (owner is not null && await db.Ingredients.CountAsync(i => i.OwnerId == owner) >= MaxPerUser)
                return Results.ValidationProblem(new Dictionary<string, string[]>
                    { ["Name"] = [$"You can add up to {MaxPerUser} ingredients of your own."] });

            var ingredient = new Ingredient { Name = name, StoreSection = request.StoreSection, OwnerId = owner };
            db.Ingredients.Add(ingredient);
            await db.SaveChangesAsync();

            return Results.Created($"/api/ingredients/{ingredient.Id}", ToResponse(ingredient));
        }).RequireAuthorization();

        group.MapPut("/{id:int}", async (int id, IngredientRequest request, AppDbContext db) =>
        {
            var errors = Validate(request);
            if (errors.Count > 0) return Results.ValidationProblem(errors);

            var ingredient = await db.Ingredients.FindAsync(id);
            if (ingredient is null) return Results.NotFound();

            var name = request.Name.Trim();
            if (await db.Ingredients.AnyAsync(i => i.Name == name && i.Id != id && i.OwnerId == ingredient.OwnerId))
                return Results.Conflict(new { message = $"An ingredient named '{name}' already exists." });

            ingredient.Name = name;
            ingredient.StoreSection = request.StoreSection;
            await db.SaveChangesAsync();
            return Results.NoContent();
        }).RequireAuthorization(Roles.Admin);

        group.MapDelete("/{id:int}", async (int id, AppDbContext db) =>
        {
            var ingredient = await db.Ingredients.FindAsync(id);
            if (ingredient is null) return Results.NotFound();

            if (await db.Set<RecipeIngredient>().AnyAsync(ri => ri.IngredientId == id))
                return Results.Conflict(new { message = "This ingredient is used by one or more recipes." });

            db.Ingredients.Remove(ingredient);
            await db.SaveChangesAsync();
            return Results.NoContent();
        }).RequireAuthorization(Roles.Admin);
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