using Microsoft.EntityFrameworkCore;
using RecipePlanner.Api.Contracts;
using RecipePlanner.Api.Data;
using RecipePlanner.Api.Models;

namespace RecipePlanner.Api.Endpoints;

public static class TechniqueEndpoints
{
    public static void MapTechniqueEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/techniques");

        group.MapGet("/", async (AppDbContext db) =>
        {
            var techniques = await db.Techniques.AsNoTracking().OrderBy(t => t.Name).ToListAsync();
            return Results.Ok(techniques.Select(ToResponse));
        });

        group.MapGet("/{id:int}", async (int id, AppDbContext db) =>
            await db.Techniques.FindAsync(id) is { } t
                ? Results.Ok(ToResponse(t))
                : Results.NotFound());

        group.MapPost("/", async (TechniqueRequest request, AppDbContext db) =>
        {
            var errors = Validate(request);
            if (errors.Count > 0) return Results.ValidationProblem(errors);

            var name = request.Name.Trim();
            if (await db.Techniques.AnyAsync(t => t.Name == name))
                return Results.Conflict(new { message = $"A technique named '{name}' already exists." });

            var technique = new Technique { Name = name, Summary = request.Summary.Trim() };
            Apply(technique, request);
            db.Techniques.Add(technique);
            await db.SaveChangesAsync();

            return Results.Created($"/api/techniques/{technique.Id}", ToResponse(technique));
        });

        group.MapPut("/{id:int}", async (int id, TechniqueRequest request, AppDbContext db) =>
        {
            var errors = Validate(request);
            if (errors.Count > 0) return Results.ValidationProblem(errors);

            var technique = await db.Techniques.FindAsync(id);
            if (technique is null) return Results.NotFound();

            var name = request.Name.Trim();
            if (await db.Techniques.AnyAsync(t => t.Name == name && t.Id != id))
                return Results.Conflict(new { message = $"A technique named '{name}' already exists." });

            Apply(technique, request);
            await db.SaveChangesAsync();
            return Results.NoContent();
        });

        group.MapDelete("/{id:int}", async (int id, AppDbContext db) =>
        {
            var technique = await db.Techniques.FindAsync(id);
            if (technique is null) return Results.NotFound();

            db.Techniques.Remove(technique);
            await db.SaveChangesAsync();
            return Results.NoContent();
        });
    }

    private static void Apply(Technique t, TechniqueRequest r)
    {
        t.Name = r.Name.Trim();
        t.Summary = r.Summary.Trim();
        t.DonenessCues = r.DonenessCues;
        t.CommonMistakes = r.CommonMistakes;
        t.Difficulty = r.Difficulty;
    }

    private static Dictionary<string, string[]> Validate(TechniqueRequest r)
    {
        var errors = new Dictionary<string, string[]>();
        if (string.IsNullOrWhiteSpace(r.Name))
            errors["Name"] = ["Name is required."];
        else if (r.Name.Length > 100)
            errors["Name"] = ["Name must be 100 characters or fewer."];
        if (string.IsNullOrWhiteSpace(r.Summary))
            errors["Summary"] = ["Summary is required."];
        if (r.Difficulty is < 1 or > 5)
            errors["Difficulty"] = ["Difficulty must be between 1 and 5."];
        return errors;
    }

    private static TechniqueResponse ToResponse(Technique t) =>
        new(t.Id, t.Name, t.Summary, t.DonenessCues, t.CommonMistakes, t.Difficulty);
}