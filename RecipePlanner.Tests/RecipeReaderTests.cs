using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Microsoft.Extensions.DependencyInjection;
using RecipePlanner.Api.Ai;
using RecipePlanner.Api.Contracts;
using RecipePlanner.Api.Data;
using static RecipePlanner.Tests.TestImages;

namespace RecipePlanner.Tests;

public class FakeRecipeReader(RawReadRecipe? result) : IRecipeReader
{
    public bool IsAvailable => true;
    public IReadOnlyList<byte[]>? LastPages { get; private set; }
    public string? LastText { get; private set; }

    public Task<RawReadRecipe?> ReadPhotosAsync(IReadOnlyList<byte[]> jpegPages, CancellationToken ct)
    {
        LastPages = jpegPages;
        return Task.FromResult(result);
    }

    public Task<RawReadRecipe?> ReadTextAsync(string text, CancellationToken ct)
    {
        LastText = text;
        return Task.FromResult(result);
    }
}

public class RecipeReaderTests
{
    private static readonly RawReadRecipe GrandmasCard = new(
        "Grandma's Beef Stew", "", 6, 20, 120,
        [
            new("Beef chuck", 2, "lb", null),
            new("Carrots", 4, "whole", null),
            new("Oleo", 2, "tbsp", null)
        ],
        [
            new("Brown the beef in the oleo.", null),
            new("Add carrots and water, cover and simmer 2 hours.", null)
        ],
        ["Oven temperature smudged; read as 350°F", "  ", "Servings not given; estimated 6", "servings not given; estimated 6"]);

    private static async Task<HttpClient> Client(ApiFactory factory)
    {
        using (var scope = factory.Services.CreateScope())
            await DbSeeder.SeedAsync(scope.ServiceProvider.GetRequiredService<AppDbContext>(),
                scope.ServiceProvider.GetRequiredService<Microsoft.AspNetCore.Hosting.IWebHostEnvironment>().ContentRootPath);
        return await factory.CreateUserClientAsync();
    }

    private static string B64(byte[] bytes) => Convert.ToBase64String(bytes);

    [Fact]
    public async Task Reads_several_photos_as_one_recipe_after_cleaning_each_photo()
    {
        var reader = new FakeRecipeReader(GrandmasCard);
        using var factory = new ApiFactory { Reader = reader };
        var client = await Client(factory);

        var front = WithExif(Jpeg(4000, 3000), orientation: 6);
        var back = Jpeg(800, 600);
        var response = await client.PostAsJsonAsync("/api/import/photos",
            new ReadPhotosRequest([B64(front), "data:image/jpeg;base64," + B64(back)]));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var result = (await response.Content.ReadFromJsonAsync<ReadRecipeResponse>())!;

        // Both pages reach the AI, in order: re-encoded, no EXIF/GPS, rotated upright and sized for reading
        Assert.Equal(2, reader.LastPages!.Count);
        Assert.All(reader.LastPages, p => Assert.False(ContainsExif(p)));
        Assert.Equal((1176, 1568), Size(reader.LastPages[0]));
        Assert.Equal((800, 600), Size(reader.LastPages[1]));

        Assert.Equal("Grandma's Beef Stew", result.Recipe.Title);
        Assert.Equal(3, result.Recipe.Ingredients.Count);
        Assert.Contains(result.Recipe.Steps, s => s.TechniqueName == "Simmering"); // guessed from the step's wording
        Assert.Equal(["Oven temperature smudged; read as 350°F", "Servings not given; estimated 6"], result.Unclear);
    }

    [Fact]
    public async Task Refuses_too_many_photos_and_files_that_are_not_images()
    {
        var reader = new FakeRecipeReader(GrandmasCard);
        using var factory = new ApiFactory { Reader = reader };
        var client = await Client(factory);
        var photo = B64(Jpeg(200, 200));

        var none = await client.PostAsJsonAsync("/api/import/photos", new ReadPhotosRequest([]));
        var tooMany = await client.PostAsJsonAsync("/api/import/photos", new ReadPhotosRequest(Enumerable.Repeat(photo, 5).ToList()));
        var notBase64 = await client.PostAsJsonAsync("/api/import/photos", new ReadPhotosRequest([photo, "not base64!!"]));
        var notImage = await client.PostAsJsonAsync("/api/import/photos", new ReadPhotosRequest([B64("<svg onload=alert(1)>"u8.ToArray())]));
        var bomb = await client.PostAsJsonAsync("/api/import/photos", new ReadPhotosRequest([B64(PngHeaderOnly(50_000, 50_000))]));

        Assert.All([none, tooMany, notBase64, notImage, bomb],
            r => Assert.Equal(HttpStatusCode.UnprocessableEntity, r.StatusCode));
        Assert.Contains("Photo 2", (await notBase64.Content.ReadAsStringAsync()));
        Assert.Null(reader.LastPages); // the AI was never called
    }

    [Fact]
    public async Task Reads_pasted_text()
    {
        var reader = new FakeRecipeReader(GrandmasCard);
        using var factory = new ApiFactory { Reader = reader };
        var client = await Client(factory);

        var pasted = "Grandma's stew\n2 lb beef chuck\n4 carrots\nBrown the beef, add carrots, simmer 2 hours.";
        var response = await client.PostAsJsonAsync("/api/import/text", new ReadTextRequest(pasted));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(pasted, reader.LastText);

        var tooShort = await client.PostAsJsonAsync("/api/import/text", new ReadTextRequest("beef stew"));
        var tooLong = await client.PostAsJsonAsync("/api/import/text", new ReadTextRequest(new string('a', RecipeReading.MaxTextLength + 1)));
        Assert.Equal(HttpStatusCode.UnprocessableEntity, tooShort.StatusCode);
        Assert.Equal(HttpStatusCode.UnprocessableEntity, tooLong.StatusCode);
    }

    [Fact]
    public async Task Explains_when_no_recipe_was_found()
    {
        using var factory = new ApiFactory { Reader = new FakeRecipeReader(new RawReadRecipe("", null, null, null, null, [], [], []) ) };
        var client = await Client(factory);

        var response = await client.PostAsJsonAsync("/api/import/photos", new ReadPhotosRequest([B64(Jpeg(300, 300))]));
        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
        Assert.Contains("Couldn't find a recipe", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Needs_login_and_an_AI_key()
    {
        using var factory = new ApiFactory(); // no API key configured in tests
        var anonymous = await factory.CreateClient().PostAsJsonAsync("/api/import/text", new ReadTextRequest("x"));
        Assert.Equal(HttpStatusCode.Unauthorized, anonymous.StatusCode);

        var client = await factory.CreateUserClientAsync();
        var response = await client.PostAsJsonAsync("/api/import/text", new ReadTextRequest("A recipe long enough to read."));
        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
    }

    [Fact]
    public void Unclear_notes_are_trimmed_capped_and_deduplicated()
    {
        var notes = RecipeReading.CleanUnclear(
            Enumerable.Range(0, 30).Select(i => $"note {i}").Prepend(new string('x', 500)).Prepend(null).Prepend("line\nbreak"));
        Assert.Equal(RecipeReading.MaxUnclear, notes.Count);
        Assert.Equal("line break", notes[0]);
        Assert.True(notes[1].Length <= RecipeReading.MaxUnclearLength + 1);
    }
}

public class SourcePageTests
{
    private record ShareResult(string ShareToken);

    private static async Task<int> NewRecipe(HttpClient client, bool? fromCookbook = null) =>
        (await (await client.PostAsJsonAsync("/api/recipes",
            new RecipeRequest("Grandma's Stew", null, null, 6, 20, 120, FromCookbook: fromCookbook)))
            .Content.ReadFromJsonAsync<RecipeResponse>())!.Id;

    private static Task<HttpResponseMessage> AddPage(HttpClient client, int id, byte[] bytes)
    {
        var content = new ByteArrayContent(bytes);
        content.Headers.ContentType = new MediaTypeHeaderValue("image/jpeg");
        return client.PostAsync($"/api/recipes/{id}/source-pages", content);
    }

    [Fact]
    public async Task Keeps_original_pages_privately_with_the_recipe()
    {
        var store = new InMemoryPhotoStore();
        using var factory = new ApiFactory { Photos = store };
        var client = await factory.CreateUserClientAsync();
        var id = await NewRecipe(client);

        var first = await AddPage(client, id, WithExif(Jpeg(3000, 4000), orientation: 1));
        var second = await AddPage(client, id, Jpeg(600, 400));
        Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        Assert.Equal(2, store.Files.Count);
        Assert.All(store.Files.Values, f => Assert.False(ContainsExif(f)));
        Assert.Contains(store.Files.Values, f => Size(f) == (1500, 2000)); // kept sharp enough to read later

        var detail = (await client.GetFromJsonAsync<RecipeDetailResponse>($"/api/recipes/{id}"))!;
        Assert.Equal([1, 2], detail.SourcePages!.Select(p => p.Position));

        var image = await client.GetAsync($"/api/recipes/{id}/source-pages/{detail.SourcePages![0].Id}");
        Assert.Equal(HttpStatusCode.OK, image.StatusCode);
        Assert.True(image.Headers.CacheControl!.Private);

        // Nobody else can see them, not even with the page's ID
        var stranger = await factory.CreateUserClientAsync();
        Assert.Equal(HttpStatusCode.NotFound, (await stranger.GetAsync($"/api/recipes/{id}/source-pages/{detail.SourcePages[0].Id}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await stranger.DeleteAsync($"/api/recipes/{id}/source-pages/{detail.SourcePages[0].Id}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await AddPage(stranger, id, Jpeg(100, 100))).StatusCode);

        // Removing one page deletes its file
        Assert.Equal(HttpStatusCode.NoContent, (await client.DeleteAsync($"/api/recipes/{id}/source-pages/{detail.SourcePages[0].Id}")).StatusCode);
        Assert.Single(store.Files);
        _ = second;
    }

    [Fact]
    public async Task Limits_pages_and_cleans_up_when_the_recipe_is_deleted()
    {
        var store = new InMemoryPhotoStore();
        using var factory = new ApiFactory { Photos = store };
        var client = await factory.CreateUserClientAsync();
        var id = await NewRecipe(client);

        for (var i = 0; i < RecipeReading.MaxPages; i++)
            Assert.Equal(HttpStatusCode.OK, (await AddPage(client, id, Jpeg(200, 200))).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await AddPage(client, id, Jpeg(200, 200))).StatusCode);

        Assert.Equal(HttpStatusCode.NoContent, (await client.DeleteAsync($"/api/recipes/{id}")).StatusCode);
        Assert.Empty(store.Files);
    }

    [Fact]
    public async Task Cookbook_recipes_stay_private()
    {
        using var factory = new ApiFactory();
        var client = await factory.CreateUserClientAsync();

        // Saved as from a cookbook: sharing is refused
        var book = await NewRecipe(client, fromCookbook: true);
        Assert.Equal(HttpStatusCode.Conflict, (await client.PostAsync($"/api/recipes/{book}/share", null)).StatusCode);
        var detail = (await client.GetFromJsonAsync<RecipeDetailResponse>($"/api/recipes/{book}"))!;
        Assert.True(detail.Social.FromCookbook);

        // Editing it (the editor doesn't send the flag) keeps it marked
        await client.PutAsJsonAsync($"/api/recipes/{book}", new RecipeRequest("Renamed", null, null, 6, 20, 120));
        Assert.Equal(HttpStatusCode.Conflict, (await client.PostAsync($"/api/recipes/{book}/share", null)).StatusCode);

        // Marking an already-shared recipe turns its link off
        var card = await NewRecipe(client);
        var token = (await (await client.PostAsync($"/api/recipes/{card}/share", null)).Content.ReadFromJsonAsync<ShareResult>())!.ShareToken;
        Assert.Equal(HttpStatusCode.OK, (await factory.CreateClient().GetAsync($"/api/shared/{token}")).StatusCode);

        Assert.Equal(HttpStatusCode.NoContent, (await client.PutAsJsonAsync($"/api/recipes/{card}/cookbook", new CookbookRequest(true))).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await factory.CreateClient().GetAsync($"/api/shared/{token}")).StatusCode);

        // And unmarking it allows sharing again
        await client.PutAsJsonAsync($"/api/recipes/{card}/cookbook", new CookbookRequest(false));
        Assert.Equal(HttpStatusCode.OK, (await client.PostAsync($"/api/recipes/{card}/share", null)).StatusCode);
    }
}
