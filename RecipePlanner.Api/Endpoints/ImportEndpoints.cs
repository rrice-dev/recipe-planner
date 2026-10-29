using System.Text.RegularExpressions;
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

            string pageText;
            try
            {
                // 1. The page's own recipe data
                if (RecipeJsonLd.Find(page.Html) is { } scraped
                    && FridgePlanner.CleanRecipe(ToRaw(scraped), [], techniques) is { } structured)
                    return Results.Ok(new ImportResponse(WithTechniques(structured, byName), page.FinalUrl.ToString(), "structured"));

                if (!extractor.IsAvailable)
                    return Fail("Couldn't find a recipe on that page.");
                pageText = PageText.From(page.Html);
            }
            catch (RegexMatchTimeoutException)
            {
                // A page built to make the parsers crawl (see ParseLimits); nothing a real recipe site does
                log.LogWarning("Import from {Host} stopped: the page took too long to read", url.Host);
                return Fail("That page is too complicated to read. Copy the recipe and use Paste text instead.");
            }

            // 2. Ask the AI to read the page text
            try
            {
                var fromAi = FridgePlanner.CleanRecipe(await extractor.ExtractAsync(pageText, ct), [], techniques);
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
        .RequireConfirmedEmail()
        .RequireRateLimiting(RateLimiting.ImportPolicy);

        // Photos of a cookbook page or a handwritten card. JSON (not a form), so other sites can't post it
        // from a logged-in user's browser; see PhotoUpload. Nothing is kept: the photos only live in memory
        // here, and the page uploads the ones you choose to keep after you save the recipe.
        app.MapPost("/api/import/photos", async (
            ReadPhotosRequest request,
            IRecipeReader reader,
            AppDbContext db,
            ILoggerFactory loggerFactory,
            CancellationToken ct) =>
        {
            if (!reader.IsAvailable)
                return Results.Problem("Reading recipes from photos isn't set up yet.", statusCode: StatusCodes.Status503ServiceUnavailable);

            var photos = request.Photos ?? [];
            if (photos.Count is 0 or > RecipeReading.MaxPages)
                return Fail($"Add between 1 and {RecipeReading.MaxPages} photos of the recipe.");

            var pages = new List<byte[]>();
            for (var i = 0; i < photos.Count; i++)
            {
                var bytes = DecodeBase64(photos[i]);
                if (bytes is null) return Fail($"Photo {i + 1} couldn't be read as an image.");

                // Same checks as every other upload: real image, sane size, re-encoded with no EXIF/GPS
                var photo = PhotoProcessor.Process(bytes, RecipeReading.ReadMaxEdge, RecipeReading.ReadJpegQuality);
                if (!photo.Success) return Fail(photos.Count == 1 ? photo.Error! : $"Photo {i + 1}: {photo.Error}");
                pages.Add(photo.Jpeg);
            }

            return await Read(() => reader.ReadPhotosAsync(pages, ct), db, loggerFactory, "photos", ct);
        })
        .RequireAuthorization()
        .RequireConfirmedEmail()
        .RequireRateLimiting(RateLimiting.RecipeReadPolicy);

        // Text pasted from anywhere: an email, a document, a site the link importer can't reach
        app.MapPost("/api/import/text", async (
            ReadTextRequest request,
            IRecipeReader reader,
            AppDbContext db,
            ILoggerFactory loggerFactory,
            CancellationToken ct) =>
        {
            if (!reader.IsAvailable)
                return Results.Problem("Reading pasted recipes isn't set up yet.", statusCode: StatusCodes.Status503ServiceUnavailable);

            var text = (request.Text ?? "").Trim();
            if (text.Length < 20) return Fail("Paste the whole recipe: the ingredients and the steps.");
            if (text.Length > RecipeReading.MaxTextLength) return Fail("That's too much text for one recipe. Paste just the recipe part.");

            return await Read(() => reader.ReadTextAsync(text, ct), db, loggerFactory, "text", ct);
        })
        .RequireAuthorization()
        .RequireConfirmedEmail()
        .RequireRateLimiting(RateLimiting.RecipeReadPolicy);
    }

    private static async Task<IResult> Read(
        Func<Task<RawReadRecipe?>> read, AppDbContext db, ILoggerFactory loggerFactory, string kind, CancellationToken ct)
    {
        RawReadRecipe? raw;
        try
        {
            raw = await read();
        }
        catch (Exception ex) when (ex is MealIdeaException or HttpRequestException or JsonException or TaskCanceledException
                                   && !ct.IsCancellationRequested)
        {
            loggerFactory.CreateLogger("ImportEndpoints").LogWarning(ex, "Reading a recipe from {Kind} failed", kind);
            return Results.Problem("Couldn't read the recipe right now. Try again in a minute.",
                statusCode: StatusCodes.Status503ServiceUnavailable);
        }

        var techniques = await db.Techniques.AsNoTracking().ToListAsync(ct);
        var byName = techniques.ToDictionary(t => t.Name, StringComparer.OrdinalIgnoreCase);
        var recipe = FridgePlanner.CleanRecipe(raw?.ToRecipe(), [], techniques);
        return recipe is null
            ? Fail(kind == "photos"
                ? "Couldn't find a recipe in those photos. Try a closer, brighter photo with the whole recipe in frame."
                : "Couldn't find a recipe in that text.")
            : Results.Ok(new ReadRecipeResponse(WithTechniques(recipe, byName), RecipeReading.CleanUnclear(raw!.Unclear)));
    }

    // Accepts plain base64 or a data: URL, which is what browsers' FileReader produces
    private static byte[]? DecodeBase64(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        var comma = value.StartsWith("data:", StringComparison.OrdinalIgnoreCase) ? value.IndexOf(',') : -1;
        var data = comma >= 0 ? value[(comma + 1)..] : value;
        // Base64 is 4/3 the size of the bytes; anything bigger can't be a photo we'd accept
        if (data.Length > (PhotoProcessor.MaxUploadBytes / 3 + 1) * 4) return null;
        var buffer = new byte[data.Length * 3 / 4 + 3];
        return Convert.TryFromBase64String(data, buffer, out var written) ? buffer[..written] : null;
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
