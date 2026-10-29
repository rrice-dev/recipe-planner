using Microsoft.Extensions.DependencyInjection;
using RecipePlanner.Api.Photos;
using System.Net;
using System.Net.Http.Json;
using RecipePlanner.Api.Contracts;

namespace RecipePlanner.Tests;

public class SettingsTests
{
    [Fact]
    public async Task New_accounts_start_with_defaults_and_settings_save()
    {
        using var factory = new ApiFactory();
        var client = await factory.CreateUserClientAsync();

        var initial = await client.GetFromJsonAsync<SettingsResponse>("/api/settings");
        Assert.Equal(new SettingsResponse("system", null, 4), initial);

        var saved = await client.PutAsJsonAsync("/api/settings", new SettingsRequest("dark", "AIP", 2));
        Assert.Equal(HttpStatusCode.OK, saved.StatusCode);
        Assert.Equal(new SettingsResponse("dark", "AIP", 2), await client.GetFromJsonAsync<SettingsResponse>("/api/settings"));

        // Settings are per account
        var other = await factory.CreateUserClientAsync();
        Assert.Equal("system", (await other.GetFromJsonAsync<SettingsResponse>("/api/settings"))!.Theme);
    }

    [Theory]
    [InlineData("purple", null, 4)]
    [InlineData("dark", "Keto", 4)]
    [InlineData("dark", null, 0)]
    [InlineData("dark", null, 101)]
    public async Task Rejects_invalid_settings(string theme, string? diet, int servings)
    {
        using var factory = new ApiFactory();
        var client = await factory.CreateUserClientAsync();
        var response = await client.PutAsJsonAsync("/api/settings", new SettingsRequest(theme, diet, servings));
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Requires_login()
    {
        using var factory = new ApiFactory();
        var client = factory.CreateClient();
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/settings")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.PostAsJsonAsync("/api/account/delete", new DeleteAccountRequest("x"))).StatusCode);
    }

    [Fact]
    public async Task Deleting_the_account_needs_the_password_and_removes_everything()
    {
        var photos = new InMemoryPhotoStore();
        using var factory = new ApiFactory { Photos = photos };
        var email = $"leaving-{Guid.NewGuid():N}@test.local";
        var client = await factory.CreateUserClientAsync(email);
        var someoneElse = await factory.CreateUserClientAsync();

        var recipe = await (await client.PostAsJsonAsync("/api/recipes", new RecipeRequest("Soup", null, null, 4, 5, 20)))
            .Content.ReadFromJsonAsync<RecipeResponse>();
        var photo = new ByteArrayContent(TestImages.Jpeg(400, 300));
        photo.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("image/jpeg");
        Assert.Equal(HttpStatusCode.OK, (await client.PutAsync($"/api/recipes/{recipe!.Id}/photo", photo)).StatusCode);
        Assert.Single(photos.Files);
        await client.PostAsJsonAsync("/api/shopping-list/items", new[] { new GroceryAddRequest("Paper towels", null, null, null) });
        var theirs = await (await someoneElse.PostAsJsonAsync("/api/recipes", new RecipeRequest("Stew", null, null, 4, 5, 20)))
            .Content.ReadFromJsonAsync<RecipeResponse>();

        var wrong = await client.PostAsJsonAsync("/api/account/delete", new DeleteAccountRequest("not-my-password"));
        Assert.Equal(HttpStatusCode.BadRequest, wrong.StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync($"/api/recipes/{recipe.Id}")).StatusCode);

        var deleted = await client.PostAsJsonAsync("/api/account/delete", new DeleteAccountRequest(TestAuth.Password));
        Assert.Equal(HttpStatusCode.NoContent, deleted.StatusCode);

        Assert.Empty(photos.Files);                                                          // photos gone from storage
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/recipes")).StatusCode); // signed out
        var login = await factory.CreateClient().PostAsJsonAsync("/api/auth/login?useCookies=true", new { email, password = TestAuth.Password });
        Assert.Equal(HttpStatusCode.Unauthorized, login.StatusCode);                         // account gone
        Assert.Equal(HttpStatusCode.OK, (await someoneElse.GetAsync($"/api/recipes/{theirs!.Id}")).StatusCode); // others untouched
    }
}

// Records whether the account still existed when each photo was deleted
public class AccountCheckingPhotoStore : InMemoryPhotoStore, IPhotoStore
{
    public Func<Task<bool>>? AccountExists { get; set; }
    public List<bool> AccountExistedAtDelete { get; } = [];

    async Task IPhotoStore.DeleteAsync(string name, CancellationToken ct)
    {
        if (AccountExists is not null) AccountExistedAtDelete.Add(await AccountExists());
        await DeleteAsync(name, ct);
    }
}

public class AccountDeleteOrderTests
{
    // Photos are removed only after the account is gone, so a failed delete never leaves an account without its photos
    [Fact]
    public async Task Photos_are_deleted_after_the_account()
    {
        var photos = new AccountCheckingPhotoStore();
        using var factory = new ApiFactory { Photos = photos };
        var email = $"order-{Guid.NewGuid():N}@test.local";
        var client = await factory.CreateUserClientAsync(email);
        photos.AccountExists = async () =>
        {
            using var scope = factory.Services.CreateScope();
            var users = scope.ServiceProvider.GetRequiredService<Microsoft.AspNetCore.Identity.UserManager<RecipePlanner.Api.Models.AppUser>>();
            return await users.FindByEmailAsync(email) is not null;
        };

        var recipe = await (await client.PostAsJsonAsync("/api/recipes", new RecipeRequest("Soup", null, null, 4, 5, 20)))
            .Content.ReadFromJsonAsync<RecipeResponse>();
        var photo = new ByteArrayContent(TestImages.Jpeg(400, 300));
        photo.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("image/jpeg");
        (await client.PutAsync($"/api/recipes/{recipe!.Id}/photo", photo)).EnsureSuccessStatusCode();

        (await client.PostAsJsonAsync("/api/account/delete", new DeleteAccountRequest(TestAuth.Password))).EnsureSuccessStatusCode();

        Assert.Equal([false], photos.AccountExistedAtDelete);
        Assert.Empty(photos.Files);
    }
}
