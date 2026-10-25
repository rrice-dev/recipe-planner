using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using RecipePlanner.Api.Contracts;
using RecipePlanner.Api.Security;
using static RecipePlanner.Tests.TestImages;

namespace RecipePlanner.Tests;

public class RecipePhotoTests
{
    private record PhotoResult(string PhotoVersion);
    private record ShareResult(string ShareToken);

    private static async Task<int> NewRecipe(HttpClient client, string title = "Lemon Chicken") =>
        (await (await client.PostAsJsonAsync("/api/recipes", new RecipeRequest(title, "Bright and easy.", null, 4, 10, 40)))
            .Content.ReadFromJsonAsync<RecipeResponse>())!.Id;

    private static Task<HttpResponseMessage> Upload(HttpClient client, int id, byte[] bytes, string type = "image/jpeg")
    {
        var content = new ByteArrayContent(bytes);
        content.Headers.ContentType = new MediaTypeHeaderValue(type);
        return client.PutAsync($"/api/recipes/{id}/photo", content);
    }

    [Fact]
    public async Task Upload_stores_a_clean_resized_photo_and_shows_it_on_the_recipe()
    {
        var store = new InMemoryPhotoStore();
        using var factory = new ApiFactory { Photos = store };
        var client = await factory.CreateUserClientAsync();
        var id = await NewRecipe(client);

        var response = await Upload(client, id, WithExif(Jpeg(3200, 2400), orientation: 6));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var saved = Assert.Single(store.Files);
        Assert.Matches("^[0-9a-f]{32}\\.jpg$", saved.Key);          // random name, not the recipe ID or file name
        Assert.False(ContainsExif(saved.Value));                    // no GPS
        Assert.Equal((1200, 1600), Size(saved.Value));               // rotated upright, long edge 1600

        var detail = await client.GetFromJsonAsync<RecipeDetailResponse>($"/api/recipes/{id}");
        Assert.NotNull(detail!.PhotoVersion);
        var list = await client.GetFromJsonAsync<List<RecipeResponse>>("/api/recipes");
        Assert.Equal(detail.PhotoVersion, list!.Single().PhotoVersion);

        var image = await client.GetAsync($"/api/recipes/{id}/photo?v={detail.PhotoVersion}");
        Assert.Equal(HttpStatusCode.OK, image.StatusCode);
        Assert.Equal("image/jpeg", image.Content.Headers.ContentType!.MediaType);
        Assert.True(image.Headers.CacheControl!.Private);   // only the user's own browser may cache it
        Assert.Contains("sandbox", image.Headers.GetValues("Content-Security-Policy").Single());
    }

    [Fact]
    public async Task Replacing_a_photo_removes_the_old_file()
    {
        var store = new InMemoryPhotoStore();
        using var factory = new ApiFactory { Photos = store };
        var client = await factory.CreateUserClientAsync();
        var id = await NewRecipe(client);

        var first = await (await Upload(client, id, Jpeg(400, 300))).Content.ReadFromJsonAsync<PhotoResult>();
        await Task.Delay(5);
        var second = await (await Upload(client, id, Jpeg(500, 300))).Content.ReadFromJsonAsync<PhotoResult>();

        Assert.Single(store.Files);
        Assert.NotEqual(first!.PhotoVersion, second!.PhotoVersion);
    }

    [Fact]
    public async Task Removing_the_photo_or_the_recipe_deletes_the_file()
    {
        var store = new InMemoryPhotoStore();
        using var factory = new ApiFactory { Photos = store };
        var client = await factory.CreateUserClientAsync();
        var a = await NewRecipe(client, "A");
        var b = await NewRecipe(client, "B");
        await Upload(client, a, Jpeg(300, 300));
        await Upload(client, b, Jpeg(300, 300));
        Assert.Equal(2, store.Files.Count);

        Assert.Equal(HttpStatusCode.NoContent, (await client.DeleteAsync($"/api/recipes/{a}/photo")).StatusCode);
        Assert.Single(store.Files);
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync($"/api/recipes/{a}/photo")).StatusCode);

        await client.DeleteAsync($"/api/recipes/{b}");
        Assert.Empty(store.Files);
    }

    [Fact]
    public async Task Other_users_cannot_see_upload_or_remove_your_photo()
    {
        var store = new InMemoryPhotoStore();
        using var factory = new ApiFactory { Photos = store };
        var owner = await factory.CreateUserClientAsync();
        var stranger = await factory.CreateUserClientAsync();
        var id = await NewRecipe(owner);
        await Upload(owner, id, Jpeg(300, 300));

        Assert.Equal(HttpStatusCode.NotFound, (await stranger.GetAsync($"/api/recipes/{id}/photo")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await Upload(stranger, id, Jpeg(300, 300))).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await stranger.DeleteAsync($"/api/recipes/{id}/photo")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await factory.CreateClient().GetAsync($"/api/recipes/{id}/photo")).StatusCode);
        Assert.Single(store.Files);
    }

    [Fact]
    public async Task Rejects_non_images_and_wrong_types_without_storing_anything()
    {
        var store = new InMemoryPhotoStore();
        using var factory = new ApiFactory { Photos = store };
        var client = await factory.CreateUserClientAsync();
        var id = await NewRecipe(client);

        Assert.Equal(HttpStatusCode.BadRequest, (await Upload(client, id, "<svg onload=alert(1)>"u8.ToArray())).StatusCode);
        Assert.Equal(HttpStatusCode.UnsupportedMediaType, (await Upload(client, id, Jpeg(100, 100), "image/svg+xml")).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await Upload(client, id, PngHeaderOnly(20_000, 20_000))).StatusCode);
        Assert.Empty(store.Files);
    }

    [Fact]
    public async Task Shared_recipes_show_their_photo_publicly_and_in_the_preview_card()
    {
        var store = new InMemoryPhotoStore();
        using var factory = new ApiFactory { Photos = store };
        var owner = await factory.CreateUserClientAsync();
        var id = await NewRecipe(owner);
        await Upload(owner, id, Jpeg(800, 600));
        var token = (await (await owner.PostAsync($"/api/recipes/{id}/share", null)).Content.ReadFromJsonAsync<ShareResult>())!.ShareToken;
        var anonymous = factory.CreateClient();

        var shared = await anonymous.GetFromJsonAsync<SharedRecipeResponse>($"/api/shared/{token}");
        Assert.NotNull(shared!.PhotoVersion);

        var image = await anonymous.GetAsync($"/api/shared/{token}/photo?v={shared.PhotoVersion}");
        Assert.Equal(HttpStatusCode.OK, image.StatusCode);
        Assert.True(image.Headers.CacheControl!.Public);

        var preview = new HttpRequestMessage(HttpMethod.Get, $"/s/{token}");
        preview.Headers.Add("X-Forwarded-Host", "recipes.example.com");
        preview.Headers.Add("X-Forwarded-Proto", "https");
        var html = await (await anonymous.SendAsync(preview)).Content.ReadAsStringAsync();
        Assert.Contains($"<meta property=\"og:image\" content=\"https://recipes.example.com/api/shared/{token}/photo?v=", html);
        Assert.Contains("summary_large_image", html);

        // Unsharing makes the photo private again
        await owner.DeleteAsync($"/api/recipes/{id}/share");
        Assert.Equal(HttpStatusCode.NotFound, (await anonymous.GetAsync($"/api/shared/{token}/photo")).StatusCode);
    }

    [Fact]
    public async Task Says_when_photo_storage_is_not_set_up()
    {
        using var factory = new ApiFactory();               // no storage configured
        var client = await factory.CreateUserClientAsync();
        var id = await NewRecipe(client);
        Assert.Equal(HttpStatusCode.ServiceUnavailable, (await Upload(client, id, Jpeg(100, 100))).StatusCode);
    }

    [Fact]
    public async Task Uploads_are_rate_limited()
    {
        using var factory = new ApiFactory { Photos = new InMemoryPhotoStore() };
        var client = await factory.CreateUserClientAsync();
        var id = await NewRecipe(client);
        var photo = Jpeg(50, 50);

        for (var i = 0; i < RateLimiting.PhotoUploadPermitLimit; i++)
            Assert.Equal(HttpStatusCode.OK, (await Upload(client, id, photo)).StatusCode);
        Assert.Equal(HttpStatusCode.TooManyRequests, (await Upload(client, id, photo)).StatusCode);
    }
}
