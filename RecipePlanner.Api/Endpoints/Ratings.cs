using Microsoft.EntityFrameworkCore;
using RecipePlanner.Api.Data;
using RecipePlanner.Api.Contracts;
using RecipePlanner.Api.Models;

namespace RecipePlanner.Api.Endpoints;

internal static class Ratings
{
    // The owner's own stars are a private note, not a vote: they never count toward the community average
    public static (double? Average, int Count) Community(IEnumerable<RecipeRating> ratings, string ownerId)
    {
        var others = ratings.Where(r => r.UserId != ownerId).ToList();
        return others.Count == 0 ? (null, 0) : (Math.Round(others.Average(r => r.Stars), 1), others.Count);
    }

    public static Dictionary<string, string[]>? Validate(RatingRequest request) =>
        request.Stars is >= 1 and <= 5
            ? null
            : new Dictionary<string, string[]> { ["Stars"] = ["Stars must be between 1 and 5."] };

    // Insert or update: each person has at most one rating per recipe, so repeat votes can't stack
    public static async Task SetAsync(AppDbContext db, int recipeId, string userId, int stars)
    {
        var existing = await db.RecipeRatings.FindAsync(recipeId, userId);
        if (existing is not null)
        {
            existing.Stars = stars;
            existing.UpdatedAt = DateTimeOffset.UtcNow;
            await db.SaveChangesAsync();
            return;
        }

        var rating = new RecipeRating { RecipeId = recipeId, UserId = userId, Stars = stars, UpdatedAt = DateTimeOffset.UtcNow };
        db.RecipeRatings.Add(rating);
        try
        {
            await db.SaveChangesAsync();
        }
        catch (DbUpdateException)
        {
            // Two clicks at once both tried to insert; the database's key let only one through. Update that one.
            db.Entry(rating).State = EntityState.Detached;
            var winner = await db.RecipeRatings.FindAsync(recipeId, userId);
            if (winner is null) throw;
            winner.Stars = stars;
            winner.UpdatedAt = DateTimeOffset.UtcNow;
            await db.SaveChangesAsync();
        }
    }

    public static async Task ClearAsync(AppDbContext db, int recipeId, string userId)
    {
        var existing = await db.RecipeRatings.FindAsync(recipeId, userId);
        if (existing is null) return;
        db.RecipeRatings.Remove(existing);
        await db.SaveChangesAsync();
    }
}

