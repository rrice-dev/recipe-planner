using System.Net;
using System.Net.Http.Json;
using RecipePlanner.Api.Contracts;

namespace RecipePlanner.Tests;

// Shared techniques and ingredients are visible to everyone,
// so only admins may change them (OWASP API5: broken function-level authorization)
public class AdminAuthorizationTests
{
    private static TechniqueRequest Technique(string name = "Searing") =>
        new(name, "High heat until a deep crust forms.", "Releases from the pan", "Crowding the pan", 2);

    [Fact]
    public async Task Regular_user_cannot_create_a_technique()
    {
        using var factory = new ApiFactory();
        var user = await factory.CreateUserClientAsync();

        var response = await user.PostAsJsonAsync("/api/techniques", Technique());
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Regular_user_cannot_edit_or_delete_a_technique()
    {
        using var factory = new ApiFactory();
        var admin = await factory.CreateAdminClientAsync();
        var user = await factory.CreateUserClientAsync();

        var created = await (await admin.PostAsJsonAsync("/api/techniques", Technique()))
            .Content.ReadFromJsonAsync<TechniqueResponse>();

        var edit = await user.PutAsJsonAsync($"/api/techniques/{created!.Id}", Technique("Vandalized"));
        Assert.Equal(HttpStatusCode.Forbidden, edit.StatusCode);

        var delete = await user.DeleteAsync($"/api/techniques/{created.Id}");
        Assert.Equal(HttpStatusCode.Forbidden, delete.StatusCode);

        var unchanged = await user.GetFromJsonAsync<TechniqueResponse>($"/api/techniques/{created.Id}");
        Assert.Equal("Searing", unchanged!.Name);
    }

    [Fact]
    public async Task Regular_user_can_add_but_not_edit_or_delete_ingredients()
    {
        using var factory = new ApiFactory();
        var user = await factory.CreateUserClientAsync();

        // Adding is allowed, since users need new ingredients for their own recipes
        var create = await user.PostAsJsonAsync("/api/ingredients", new IngredientRequest("Shallot", "Produce"));
        Assert.Equal(HttpStatusCode.Created, create.StatusCode);
        var shallot = await create.Content.ReadFromJsonAsync<IngredientResponse>();

        var edit = await user.PutAsJsonAsync($"/api/ingredients/{shallot!.Id}", new IngredientRequest("Garbage", null));
        Assert.Equal(HttpStatusCode.Forbidden, edit.StatusCode);

        var delete = await user.DeleteAsync($"/api/ingredients/{shallot.Id}");
        Assert.Equal(HttpStatusCode.Forbidden, delete.StatusCode);
    }

    [Fact]
    public async Task Admin_can_edit_a_technique()
    {
        using var factory = new ApiFactory();
        var admin = await factory.CreateAdminClientAsync();

        var created = await (await admin.PostAsJsonAsync("/api/techniques", Technique()))
            .Content.ReadFromJsonAsync<TechniqueResponse>();

        var edit = await admin.PutAsJsonAsync($"/api/techniques/{created!.Id}", Technique("Pan-searing"));
        Assert.Equal(HttpStatusCode.NoContent, edit.StatusCode);
    }
}
