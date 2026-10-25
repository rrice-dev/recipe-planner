using System.Net;
using System.Net.Http.Json;
using RecipePlanner.Api.Contracts;

namespace RecipePlanner.Tests;

public class RatingTests
{
    private record ShareResult(string ShareToken);

    private static async Task<(int Id, string Token)> SharedRecipe(HttpClient owner)
    {
        var recipe = await (await owner.PostAsJsonAsync("/api/recipes",
            new RecipeRequest("Pad Thai", null, "Thai", 2, 15, 15))).Content.ReadFromJsonAsync<RecipeResponse>();
        var share = await (await owner.PostAsync($"/api/recipes/{recipe!.Id}/share", null)).Content.ReadFromJsonAsync<ShareResult>();
        return (recipe.Id, share!.ShareToken);
    }

    private static Task<SharedRecipeResponse?> View(HttpClient client, string token) =>
        client.GetFromJsonAsync<SharedRecipeResponse>($"/api/shared/{token}");

    [Fact]
    public async Task Owner_can_privately_rate_their_own_recipe()
    {
        using var factory = new ApiFactory();
        var owner = await factory.CreateUserClientAsync();
        var recipe = await (await owner.PostAsJsonAsync("/api/recipes",
            new RecipeRequest("Weeknight Pasta", null, null, 2, 5, 15))).Content.ReadFromJsonAsync<RecipeResponse>();

        Assert.Equal(HttpStatusCode.NoContent,
            (await owner.PutAsJsonAsync($"/api/recipes/{recipe!.Id}/rating", new RatingRequest(4))).StatusCode);

        var detail = await owner.GetFromJsonAsync<RecipeDetailResponse>($"/api/recipes/{recipe.Id}");
        Assert.Equal(4, detail!.Social.MyRating);
        Assert.Equal(0, detail.Social.CommunityCount);      // your own stars aren't a community vote
    }

    [Fact]
    public async Task Community_average_counts_one_vote_per_person_and_excludes_the_owner()
    {
        using var factory = new ApiFactory();
        var owner = await factory.CreateUserClientAsync();
        var alice = await factory.CreateUserClientAsync();
        var bob = await factory.CreateUserClientAsync();
        var (id, token) = await SharedRecipe(owner);

        await owner.PutAsJsonAsync($"/api/shared/{token}/rating", new RatingRequest(5));  // owner rating self
        await alice.PutAsJsonAsync($"/api/shared/{token}/rating", new RatingRequest(1));
        await alice.PutAsJsonAsync($"/api/shared/{token}/rating", new RatingRequest(4));  // changes her vote, not a second one
        await bob.PutAsJsonAsync($"/api/shared/{token}/rating", new RatingRequest(3));

        var view = await View(alice, token);
        Assert.Equal(2, view!.Rating.Count);
        Assert.Equal(3.5, view.Rating.Average);
        Assert.Equal(4, view.Rating.MyRating);
        Assert.False(view.Rating.IsOwner);

        var detail = await owner.GetFromJsonAsync<RecipeDetailResponse>($"/api/recipes/{id}");
        Assert.Equal(5, detail!.Social.MyRating);
        Assert.Equal(3.5, detail.Social.CommunityAverage);
    }

    [Fact]
    public async Task Rating_requires_login()
    {
        using var factory = new ApiFactory();
        var owner = await factory.CreateUserClientAsync();
        var (_, token) = await SharedRecipe(owner);

        var response = await factory.CreateClient().PutAsJsonAsync($"/api/shared/{token}/rating", new RatingRequest(5));
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(6)]
    [InlineData(-1)]
    [InlineData(1000)]
    public async Task Stars_must_be_1_to_5(int stars)
    {
        using var factory = new ApiFactory();
        var owner = await factory.CreateUserClientAsync();
        var voter = await factory.CreateUserClientAsync();
        var (_, token) = await SharedRecipe(owner);

        var response = await voter.PutAsJsonAsync($"/api/shared/{token}/rating", new RatingRequest(stars));
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Ratings_can_be_cleared()
    {
        using var factory = new ApiFactory();
        var owner = await factory.CreateUserClientAsync();
        var voter = await factory.CreateUserClientAsync();
        var (_, token) = await SharedRecipe(owner);

        await voter.PutAsJsonAsync($"/api/shared/{token}/rating", new RatingRequest(2));
        Assert.Equal(HttpStatusCode.NoContent, (await voter.DeleteAsync($"/api/shared/{token}/rating")).StatusCode);

        var view = await View(voter, token);
        Assert.Null(view!.Rating.MyRating);
        Assert.Equal(0, view.Rating.Count);
    }

    [Fact]
    public async Task Cannot_rate_through_a_dead_link()
    {
        using var factory = new ApiFactory();
        var owner = await factory.CreateUserClientAsync();
        var voter = await factory.CreateUserClientAsync();
        var (id, token) = await SharedRecipe(owner);
        await owner.DeleteAsync($"/api/recipes/{id}/share");

        var response = await voter.PutAsJsonAsync($"/api/shared/{token}/rating", new RatingRequest(5));
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }
}
