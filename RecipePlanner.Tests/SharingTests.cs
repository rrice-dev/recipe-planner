using System.Net;
using System.Net.Http.Json;
using RecipePlanner.Api.Contracts;

namespace RecipePlanner.Tests;

public class SharingTests
{
    private record ShareResult(string ShareToken);

    private static async Task<(int Id, string Token)> CreateSharedRecipe(HttpClient owner, string title = "Grandma's Chili")
    {
        var recipe = await (await owner.PostAsJsonAsync("/api/recipes",
            new RecipeRequest(title, "Smoky and slow.", "American", 6, 20, 120))).Content.ReadFromJsonAsync<RecipeResponse>();
        var share = await (await owner.PostAsync($"/api/recipes/{recipe!.Id}/share", null)).Content.ReadFromJsonAsync<ShareResult>();
        return (recipe.Id, share!.ShareToken);
    }

    [Fact]
    public async Task Share_tokens_are_long_and_random()
    {
        using var factory = new ApiFactory();
        var owner = await factory.CreateUserClientAsync();
        var (_, a) = await CreateSharedRecipe(owner, "One");
        var (_, b) = await CreateSharedRecipe(owner, "Two");

        Assert.Equal(22, a.Length);
        Assert.NotEqual(a, b);
    }

    [Fact]
    public async Task Anyone_with_the_link_can_view_without_logging_in()
    {
        using var factory = new ApiFactory();
        var owner = await factory.CreateUserClientAsync("chef@test.local");
        var (_, token) = await CreateSharedRecipe(owner);

        var response = await factory.CreateClient().GetAsync($"/api/shared/{token}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var json = await response.Content.ReadAsStringAsync();
        Assert.Contains("Grandma's Chili", json);
        Assert.DoesNotContain("chef@test.local", json);     // no owner details leak
        Assert.DoesNotContain("ownerId", json, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Sharing_twice_keeps_the_same_link()
    {
        using var factory = new ApiFactory();
        var owner = await factory.CreateUserClientAsync();
        var (id, token) = await CreateSharedRecipe(owner);

        var again = await (await owner.PostAsync($"/api/recipes/{id}/share", null)).Content.ReadFromJsonAsync<ShareResult>();
        Assert.Equal(token, again!.ShareToken);
    }

    [Fact]
    public async Task Turning_sharing_off_kills_the_link_and_resharing_makes_a_new_one()
    {
        using var factory = new ApiFactory();
        var owner = await factory.CreateUserClientAsync();
        var (id, oldToken) = await CreateSharedRecipe(owner);

        Assert.Equal(HttpStatusCode.NoContent, (await owner.DeleteAsync($"/api/recipes/{id}/share")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await factory.CreateClient().GetAsync($"/api/shared/{oldToken}")).StatusCode);

        var fresh = await (await owner.PostAsync($"/api/recipes/{id}/share", null)).Content.ReadFromJsonAsync<ShareResult>();
        Assert.NotEqual(oldToken, fresh!.ShareToken);
        Assert.Equal(HttpStatusCode.NotFound, (await factory.CreateClient().GetAsync($"/api/shared/{oldToken}")).StatusCode);
    }

    [Fact]
    public async Task Only_the_owner_can_share_or_unshare()
    {
        using var factory = new ApiFactory();
        var owner = await factory.CreateUserClientAsync();
        var stranger = await factory.CreateUserClientAsync();
        var (id, token) = await CreateSharedRecipe(owner);

        Assert.Equal(HttpStatusCode.NotFound, (await stranger.PostAsync($"/api/recipes/{id}/share", null)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await stranger.DeleteAsync($"/api/recipes/{id}/share")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await factory.CreateClient().GetAsync($"/api/shared/{token}")).StatusCode);
    }

    [Theory]
    [InlineData("1")]                                   // looks like an ID
    [InlineData("aaaaaaaaaaaaaaaaaaaaaa")]              // right shape, but not a real token
    [InlineData("..%2F..%2Fetc%2Fpasswd")]
    public async Task Unknown_or_malformed_tokens_are_404(string token)
    {
        using var factory = new ApiFactory();
        var response = await factory.CreateClient().GetAsync($"/api/shared/{token}");
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Unshared_recipes_cannot_be_viewed_or_rated_by_others()
    {
        using var factory = new ApiFactory();
        var owner = await factory.CreateUserClientAsync();
        var stranger = await factory.CreateUserClientAsync();
        var recipe = await (await owner.PostAsJsonAsync("/api/recipes",
            new RecipeRequest("Private Stew", null, null, 2, 5, 30))).Content.ReadFromJsonAsync<RecipeResponse>();

        Assert.Equal(HttpStatusCode.NotFound, (await stranger.GetAsync($"/api/recipes/{recipe!.Id}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound,
            (await stranger.PutAsJsonAsync($"/api/recipes/{recipe.Id}/rating", new RatingRequest(1))).StatusCode);
    }

    [Fact]
    public async Task Preview_page_has_social_tags_and_encodes_the_title()
    {
        using var factory = new ApiFactory();
        var owner = await factory.CreateUserClientAsync();
        var (_, token) = await CreateSharedRecipe(owner, "Tacos <script>alert('x')</script> & \"Salsa\"");

        var response = await factory.CreateClient().GetAsync($"/s/{token}");
        var html = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.StartsWith("text/html", response.Content.Headers.ContentType!.ToString());
        Assert.Contains("<meta property=\"og:title\" content=\"Tacos &lt;script&gt;", html);
        Assert.DoesNotContain("<script>", html);
        Assert.Contains($"url=/r/{token}", html);
        Assert.Contains("default-src 'none'", response.Headers.GetValues("Content-Security-Policy").Single());
    }

    [Fact]
    public async Task Preview_page_for_a_dead_link_is_404()
    {
        using var factory = new ApiFactory();
        var response = await factory.CreateClient().GetAsync("/s/aaaaaaaaaaaaaaaaaaaaaa");
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }
}
