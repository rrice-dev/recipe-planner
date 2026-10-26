using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using RecipePlanner.Api.Ai;
using RecipePlanner.Api.Contracts;
using RecipePlanner.Api.Data;
using RecipePlanner.Api.Import;
using RecipePlanner.Api.Security;

namespace RecipePlanner.Api.Endpoints;

public static class ImportEndpoints
{
    public static void MapImportEndpoints(this IEndpointRouteBuilder app)
    {
        // Reads a recipe from a web page and returns it for review. Nothing is saved here:
        // the page saves it through the normal recipe endpoints once you've looked it over.
        app.MapPost("/api/import", async (
            ImportRequest request,
            IPageFetcher fetcher,
            IRecipeExtractor extractor,
            AppDbContext db,
            ILoggerFactory loggerFactory,
            CancellationToken ct) =>
        {
            var raw = (request.Url ?? "").Trim();
            if (raw.Length is 0 or > RecipeLimits.MaxSourceUrlLength || !Uri.TryCreate(raw, UriKind.Absolute, out var url))
                return Fail("Paste the full link to a recipe page, starting with https://.");

            var log = loggerFactory.CreateLogger("ImportEndpoints");
            FetchedPage page;
            try
            {
                SafePageFetcher.CheckUrl(url);
                page = await fetcher.FetchAsync(url, ct);
            }
            catch (ImportException ex)
            {
                // Log the host only; full URLs can contain personal tokens
                log.LogInformation("Import from {Host} failed: {Reason}", url.Host, ex.Message);
                return Fail(ex.Message);
            }

            var techniques = await db.Techniques.AsNoTracking().ToListAsync(ct);
            var byName = techniques.ToDictionary(t => t.Name, StringComparer.OrdinalIgnoreCase);

            // 1. The page's own recipe data
            if (RecipeJsonLd.Find(page.Html) is { } scraped
                && FridgePlanner.CleanRecipe(ToRaw(scraped), [], techniques) is { } structured)
                return Results.Ok(new ImportResponse(WithTechniques(structured, byName), page.FinalUrl.ToString(), "structured"));

            // 2. Ask the AI to read the page text
            if (!extractor.IsAvailable)
                return Fail("Couldn't find a recipe on that page.");
            try
            {
                var fromAi = FridgePlanner.CleanRecipe(await extractor.ExtractAsync(PageText.From(page.Html), ct), [], techniques);
                return fromAi is null
                    ? Fail("Couldn't find a recipe on that page.")
                    : Results.Ok(new ImportResponse(WithTechniques(fromAi, byName), page.FinalUrl.ToString(), "ai"));
            }
            catch (Exception ex) when (ex is MealIdeaException or HttpRequestException or JsonException or TaskCanceledException
                                       && !ct.IsCancellationRequested)
            {
                log.LogWarning(ex, "AI recipe extraction failed for {Host}", url.Host);
                return Fail("Couldn't read a recipe from that page right now. Try again in a minute.");
            }
        })
        .RequireAuthorization()
        .RequireRateLimiting(RateLimiting.ImportPolicy);
    }

    private static IResult Fail(string message) =>
        Results.Problem(message, statusCode: StatusCodes.Status422UnprocessableEntity);

    private static RawRecipe ToRaw(ScrapedRecipe s) => new(
        s.Title, s.Description, s.Servings, s.PrepMinutes, s.CookMinutes,
        s.IngredientLines.Select(IngredientLineParser.Parse)
            .Select(i => new RawRecipeIngredient(i.Name, i.Quantity, i.Unit, i.Note)).ToList(),
        s.Steps.Select(step => new RawRecipeStep(step, null)).ToList());

    // Steps the source didn't label get a technique from the verbs they use
    private static RecipeDraftResponse WithTechniques(RecipeDraftResponse r, IReadOnlyDictionary<string, Models.Technique> byName) =>
        r with
        {
            Steps = r.Steps.Select(s => s.TechniqueId is not null ? s
                : TechniqueGuesser.Guess(s.Instruction, byName) is { } t ? s with { TechniqueId = t.Id, TechniqueName = t.Name } : s).ToList()
        };
}
