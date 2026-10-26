using System.Security.Claims;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using RecipePlanner.Api.Contracts;
using RecipePlanner.Api.Data;
using RecipePlanner.Api.Library;
using RecipePlanner.Api.Models;

namespace RecipePlanner.Api.Endpoints;

public static class MealPlanEndpoints
{
    public static void MapMealPlanEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/mealplans").RequireAuthorization();

        group.MapGet("/", async (ClaimsPrincipal user, AppDbContext db) =>
        {
            var userId = user.UserId();
            var plans = await db.MealPlans.AsNoTracking()
                .Where(p => p.OwnerId == userId)
                .Include(p => p.Entries).ThenInclude(e => e.Recipe)
                .OrderByDescending(p => p.WeekStart)
                .ToListAsync();
            return Results.Ok(plans.Select(ToResponse));
        });

        group.MapGet("/{id:int}", async (int id, ClaimsPrincipal user, AppDbContext db) =>
        {
            var userId = user.UserId();
            var plan = await db.MealPlans.AsNoTracking()
                .Include(p => p.Entries).ThenInclude(e => e.Recipe)
                .FirstOrDefaultAsync(p => p.Id == id && p.OwnerId == userId);
            return plan is null ? Results.NotFound() : Results.Ok(ToResponse(plan));
        });

        group.MapPost("/", async (MealPlanRequest request, ClaimsPrincipal user, AppDbContext db) =>
        {
            if (string.IsNullOrWhiteSpace(request.Name) || request.Name.Length > 100)
                return Results.ValidationProblem(new Dictionary<string, string[]>
                    { ["Name"] = ["Name is required (100 characters max)."] });

            var plan = new MealPlan
            {
                Name = request.Name.Trim(),
                WeekStart = request.WeekStart,
                OwnerId = user.UserId()
            };
            db.MealPlans.Add(plan);
            await db.SaveChangesAsync();
            return Results.Created($"/api/mealplans/{plan.Id}", ToResponse(plan));
        });

        // "Plan my week": suggest dinners from the library and the user's own recipes. Nothing is saved yet.
        group.MapPost("/suggest", async (PlanSuggestRequest request, ClaimsPrincipal user, AppDbContext db,
            RecipeLibrary library, UserManager<AppUser> users) =>
        {
            var errors = new Dictionary<string, string[]>();
            if (request.Count is < 1 or > WeekPlanner.MaxDays)
                errors["Count"] = [$"Count must be between 1 and {WeekPlanner.MaxDays}."];
            var source = request.Source ?? "both";
            if (source is not ("both" or "library" or "mine"))
                errors["Source"] = ["Source must be both, library or mine."];
            if (request.Diet is not null && !Diet.IsKnown(request.Diet))
                errors["Diet"] = ["Unknown diet."];
            if (errors.Count > 0) return Results.ValidationProblem(errors);

            var userId = user.UserId();
            // No diet in the request means "use my setting", unless the caller turned that off
            var diet = request.Diet ?? (request.UseMyDiet ? (await users.GetUserAsync(user))?.Diet : null);

            var options = new List<WeekPlanner.Option>();
            if (source is "both" or "mine")
            {
                var mine = await db.Recipes.AsNoTracking()
                    .Where(r => r.OwnerId == userId)
                    .Include(r => r.Ingredients).ThenInclude(ri => ri.Ingredient)
                    .AsSplitQuery()
                    .ToListAsync();
                options.AddRange(mine.Select(r => WeekPlanner.FromMine(r, RecipePhotos.Version(r))));
            }
            if (source is "both" or "library")
                options.AddRange(library.All.Select(WeekPlanner.FromLibrary));

            var (picks, matching) = WeekPlanner.Suggest(options, request.Count, diet, Blank(request.Tag), Blank(request.Cuisine),
                request.Exclude ?? [], Random.Shared);
            return Results.Ok(new PlanSuggestResponse(picks, matching, diet));
        });

        // Saves a planned week. Library picks become the user's own recipes, reusing an earlier copy if there is one.
        group.MapPost("/plan", async (CreatePlanRequest request, ClaimsPrincipal user, AppDbContext db, RecipeLibrary library) =>
        {
            var errors = new Dictionary<string, string[]>();
            if (string.IsNullOrWhiteSpace(request.Name) || request.Name.Trim().Length > 100)
                errors["Name"] = ["Name is required (100 characters max)."];
            var picks = request.Picks ?? [];
            if (picks.Count is 0 or > WeekPlanner.MaxDays)
                errors["Picks"] = [$"Pick between 1 and {WeekPlanner.MaxDays} meals."];
            var weekEnd = request.WeekStart.AddDays(6);
            if (picks.Any(p => p.Day < request.WeekStart || p.Day > weekEnd))
                errors["Day"] = [$"Days must fall between {request.WeekStart} and {weekEnd}."];
            if (picks.Any(p => p.Servings is < 1 or > UserSettings.MaxServings))
                errors["Servings"] = [$"Servings must be between 1 and {UserSettings.MaxServings}."];
            if (errors.Count > 0) return Results.ValidationProblem(errors);

            var userId = user.UserId();
            var myIds = picks.Where(p => p.Key.StartsWith("mine:")).Select(p => int.TryParse(p.Key[5..], out var id) ? id : -1).ToHashSet();
            var mine = await db.Recipes.Where(r => r.OwnerId == userId && (myIds.Contains(r.Id) || r.LibrarySlug != null))
                .ToListAsync();

            var resolved = new Dictionary<string, Recipe>();
            foreach (var key in picks.Select(p => p.Key).Distinct())
            {
                Recipe? recipe = null;
                if (key.StartsWith("mine:") && int.TryParse(key[5..], out var id))
                    recipe = mine.FirstOrDefault(r => r.Id == id);
                else if (key.StartsWith("lib:") && library.Find(key[4..]) is { } source)
                    recipe = mine.FirstOrDefault(r => r.LibrarySlug == source.Slug)
                             ?? await LibraryEndpoints.CopyAsync(db, library, source, userId);

                if (recipe is null)
                    return Results.ValidationProblem(new Dictionary<string, string[]> { ["Key"] = [$"Unknown recipe: {key}"] });
                resolved[key] = recipe;
            }

            var plan = new MealPlan
            {
                Name = request.Name.Trim(),
                WeekStart = request.WeekStart,
                OwnerId = userId,
                Entries = picks.Select(p => new MealPlanEntry { Day = p.Day, Recipe = resolved[p.Key], Servings = p.Servings }).ToList()
            };
            db.MealPlans.Add(plan);
            await db.SaveChangesAsync();
            return Results.Created($"/api/mealplans/{plan.Id}", ToResponse(plan));
        });

        group.MapDelete("/{id:int}", async (int id, ClaimsPrincipal user, AppDbContext db) =>
        {
            var userId = user.UserId();
            var plan = await db.MealPlans.FirstOrDefaultAsync(p => p.Id == id && p.OwnerId == userId);
            if (plan is null) return Results.NotFound();

            db.MealPlans.Remove(plan);
            await db.SaveChangesAsync();
            return Results.NoContent();
        });

        group.MapPut("/{id:int}/entries",
            async (int id, List<MealPlanEntryRequest> entries, ClaimsPrincipal user, AppDbContext db) =>
        {
            var userId = user.UserId();
            var plan = await db.MealPlans.Include(p => p.Entries)
                .FirstOrDefaultAsync(p => p.Id == id && p.OwnerId == userId);
            if (plan is null) return Results.NotFound();

            var errors = new Dictionary<string, string[]>();
            var weekEnd = plan.WeekStart.AddDays(6);
            if (entries.Any(e => e.Day < plan.WeekStart || e.Day > weekEnd))
                errors["Day"] = [$"Days must fall between {plan.WeekStart} and {weekEnd}."];
            if (entries.Any(e => e.Servings < 1))
                errors["Servings"] = ["Servings must be at least 1."];
            if (errors.Count > 0) return Results.ValidationProblem(errors);

            // Only the user's OWN recipes can go in their plan
            var recipeIds = entries.Select(e => e.RecipeId).Distinct().ToList();
            var found = await db.Recipes.CountAsync(r => recipeIds.Contains(r.Id) && r.OwnerId == userId);
            if (found != recipeIds.Count)
                return Results.ValidationProblem(new Dictionary<string, string[]>
                    { ["RecipeId"] = ["One or more recipes do not exist."] });

            plan.Entries.Clear();
            foreach (var e in entries)
                plan.Entries.Add(new MealPlanEntry { Day = e.Day, RecipeId = e.RecipeId, Servings = e.Servings });

            await db.SaveChangesAsync();
            return Results.NoContent();
        });

        // Prep day: one session to batch-cook or prep the week. ?prepDay=2026-11-01 (defaults to the day before the week starts)
        group.MapGet("/{id:int}/prep", async (int id, DateOnly? prepDay, ClaimsPrincipal user, AppDbContext db) =>
        {
            var userId = user.UserId();
            var plan = await db.MealPlans.AsNoTracking()
                .Include(p => p.Entries).ThenInclude(e => e.Recipe).ThenInclude(r => r.Ingredients).ThenInclude(ri => ri.Ingredient)
                .Include(p => p.Entries).ThenInclude(e => e.Recipe).ThenInclude(r => r.Steps).ThenInclude(s => s.Technique)
                .Include(p => p.Entries).ThenInclude(e => e.Recipe).ThenInclude(r => r.Techniques)
                .AsSplitQuery()
                .FirstOrDefaultAsync(p => p.Id == id && p.OwnerId == userId);
            if (plan is null) return Results.NotFound();

            var day = prepDay ?? plan.WeekStart.AddDays(-1);
            if (day > plan.WeekStart.AddDays(6) || day < plan.WeekStart.AddDays(-7))
                return Results.ValidationProblem(new Dictionary<string, string[]> { ["PrepDay"] = ["Pick a prep day close to the planned week."] });

            var session = PrepPlanner.Build(plan.Entries.Select(e => new PrepPlanner.PlannedMeal(e.Day, e.Recipe, e.Servings)).ToList(), day);
            return Results.Ok(new PrepSessionResponse(plan.Id, plan.Name, session.PrepDay,
                session.Meals.Select(m => new PrepMealResponse(m.Day, m.RecipeId, m.Title, m.Servings, m.Approach.ToString(), m.Reason)).ToList(),
                session.StartFirst.Select(l => new LongCookResponse(l.RecipeId, l.Title, l.Minutes, l.FirstStep)).ToList(),
                session.ChopOnce.Select(c => new PrepItemResponse(c.Ingredient, c.Prep, c.Quantity, c.Unit, c.Section, c.ForRecipes)).ToList(),
                session.Marinate));
        });

        group.MapGet("/{id:int}/shopping-list", async (int id, ClaimsPrincipal user, AppDbContext db) =>
        {
            var userId = user.UserId();
            var plan = await db.MealPlans.AsNoTracking()
                .Include(p => p.Entries)
                    .ThenInclude(e => e.Recipe)
                    .ThenInclude(r => r.Ingredients)
                    .ThenInclude(ri => ri.Ingredient)
                .AsSplitQuery()
                .FirstOrDefaultAsync(p => p.Id == id && p.OwnerId == userId);
            if (plan is null) return Results.NotFound();

            var items = plan.Entries
                .SelectMany(e => e.Recipe.Ingredients.Select(ri => new
                {
                    ri.Ingredient.Name,
                    ri.Ingredient.StoreSection,
                    Unit = ri.Unit.ToLowerInvariant(),
                    Quantity = ri.Quantity * e.Servings / Math.Max(e.Recipe.Servings, 1)
                }))
                .GroupBy(x => new { x.Name, x.Unit, x.StoreSection })
                .Select(g => new ShoppingListItem(
                    g.Key.Name, Math.Round(g.Sum(x => x.Quantity), 2), g.Key.Unit, g.Key.StoreSection))
                .OrderBy(i => i.StoreSection ?? "~")
                .ThenBy(i => i.Ingredient)
                .ToList();

            return Results.Ok(new ShoppingListResponse(plan.Id, plan.Name, items));
        });
    }

    private static string? Blank(string? s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim();

    private static MealPlanResponse ToResponse(MealPlan p) =>
        new(p.Id, p.Name, p.WeekStart,
            p.Entries.OrderBy(e => e.Day)
                .Select(e => new MealPlanEntryResponse(e.Day, e.RecipeId, e.Recipe?.Title ?? "", e.Servings))
                .ToList());
}