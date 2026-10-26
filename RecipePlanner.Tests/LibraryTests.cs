using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using RecipePlanner.Api.Contracts;
using RecipePlanner.Api.Data;
using RecipePlanner.Api.Library;

namespace RecipePlanner.Tests;

public class LibraryDataTests
{
    private static string ApiContentRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !Directory.Exists(Path.Combine(dir.FullName, "RecipePlanner.Api")))
            dir = dir.Parent;
        return Path.Combine(dir!.FullName, "RecipePlanner.Api");
    }

    private static readonly RecipeLibrary Library = RecipeLibrary.Load(ApiContentRoot());

    private static HashSet<string> TechniqueNames()
    {
        var json = File.ReadAllText(Path.Combine(ApiContentRoot(), "Data", "Seed", "techniques.json"));
        return JsonDocument.Parse(json).RootElement.EnumerateArray()
            .Select(t => t.GetProperty("name").GetString()!).ToHashSet(StringComparer.OrdinalIgnoreCase);
    }

    private static readonly string[] AllowedTags = ["Asian", "AIP", "Vegetarian", "Breakfast", "Side", "Soup", "Dessert"];

    [Fact]
    public void Library_has_a_good_spread_of_recipes()
    {
        Assert.True(Library.All.Count >= 50);
        Assert.True(Library.All.Count(r => r.Tags.Contains("Asian")) >= 25);
        Assert.True(Library.All.Count(r => r.Tags.Contains("AIP")) >= 15);
        Assert.True(Library.All.Count(r => r.Tags.Contains("AIP") && r.Tags.Contains("Asian")) >= 8);
    }

    [Fact]
    public void Every_recipe_is_well_formed()
    {
        Assert.Equal(Library.All.Count, Library.All.Select(r => r.Slug.ToLowerInvariant()).Distinct().Count());
        Assert.All(Library.All, r =>
        {
            Assert.Matches("^[a-z0-9]+(-[a-z0-9]+)*$", r.Slug);
            Assert.False(string.IsNullOrWhiteSpace(r.Title));
            Assert.True(r.Title.Length <= 200);
            Assert.False(string.IsNullOrWhiteSpace(r.Description));
            Assert.True(r.Servings >= 1);
            Assert.True(r.PrepMinutes >= 0 && r.CookMinutes >= 0);
            Assert.NotEmpty(r.Ingredients);
            Assert.NotEmpty(r.Steps);
            Assert.All(r.Tags, t => Assert.Contains(t, AllowedTags));
            Assert.All(r.Ingredients, i =>
            {
                Assert.True(i.Quantity > 0, $"{r.Slug}: {i.Name} quantity");
                Assert.False(string.IsNullOrWhiteSpace(i.Unit));
                Assert.True(i.Unit.Length <= 20);
            });
            Assert.Equal(r.Ingredients.Count, r.Ingredients.Select(i => i.Name.ToLowerInvariant()).Distinct().Count());
        });
    }

    [Fact]
    public void Every_ingredient_and_technique_exists_in_the_seed_data()
    {
        var techniques = TechniqueNames();
        foreach (var r in Library.All)
        {
            foreach (var i in r.Ingredients)
                Assert.True(Library.SeedSections.ContainsKey(i.Name), $"{r.Slug}: '{i.Name}' is not a seed ingredient");
            foreach (var s in r.Steps.Where(s => s.Technique is not null))
                Assert.True(techniques.Contains(s.Technique!), $"{r.Slug}: '{s.Technique}' is not a technique");
        }
    }

    // The rules live in the app (Library/Diet.cs) so the week planner checks your own recipes the same way
    [Fact]
    public void AIP_recipes_contain_nothing_off_the_elimination_list()
    {
        foreach (var r in Library.All.Where(r => r.Tags.Contains(Diet.Aip)))
        foreach (var i in r.Ingredients)
            Assert.True(Diet.FitsAip(i.Name, Library.SeedSections[i.Name]), $"{r.Slug}: '{i.Name}' is not AIP");
    }

    [Theory]
    [InlineData("Black Pepper", "Spices")]
    [InlineData("Bell Pepper", "Produce")]
    [InlineData("Soy Sauce", "Condiments & Sauces")]
    [InlineData("Sesame Oil", "Oils & Vinegars")]
    [InlineData("Eggs", "Dairy")]
    [InlineData("Long-Grain White Rice", "Grains & Pasta")]
    [InlineData("Frozen Peas", "Frozen")]
    [InlineData("Smoked Paprika", "Spices")]
    [InlineData("Brown Sugar", "Baking")]
    [InlineData("Ground Cumin", "Spices")]
    [InlineData("Vegetable Oil", "Oils & Vinegars")]
    [InlineData("Rice Vinegar", "Oils & Vinegars")]
    [InlineData("Berbere Spice", "Spices")]
    [InlineData("Kecap Manis", "Condiments & Sauces")]
    public void AIP_check_catches_the_common_mistakes(string name, string section)
    {
        // Guards the guard: make sure the rules really flag these
        Assert.False(Diet.FitsAip(name, section));
        Assert.False(Diet.FitsAip(name, null)); // even when the store section is unknown (a user's own ingredient)
    }

    [Theory]
    [InlineData("Coconut Flour")]
    [InlineData("Sweet Potato")]
    [InlineData("Cauliflower")]
    [InlineData("Coconut Aminos")]
    [InlineData("Chicken Thighs")]
    public void AIP_check_allows_the_usual_swaps(string name) => Assert.True(Diet.FitsAip(name, null));

    [Fact]
    public void Vegetarian_recipes_have_no_meat_or_fish()
    {
        foreach (var r in Library.All.Where(r => r.Tags.Contains(Diet.Vegetarian)))
        foreach (var i in r.Ingredients)
            Assert.True(Diet.FitsVegetarian(i.Name, Library.SeedSections[i.Name]), $"{r.Slug}: '{i.Name}' is not vegetarian");
    }

    [Theory]
    [InlineData("Chicken Bone Broth", false)]
    [InlineData("Anchovies", false)]
    [InlineData("Smoked Salmon", false)]
    [InlineData("Champagne Vinegar", true)]
    [InlineData("Coconut Milk", true)]
    public void Vegetarian_check_uses_whole_words(string name, bool vegetarian) =>
        Assert.Equal(vegetarian, Diet.FitsVegetarian(name, null));

    [Fact]
    public void Quick_tag_is_computed_from_total_time()
    {
        Assert.All(Library.All, r =>
            Assert.Equal(r.PrepMinutes + r.CookMinutes <= 30, RecipeLibrary.TagsFor(r).Contains("Quick")));
    }
}

public class LibraryEndpointTests
{
    [Fact]
    public async Task Anyone_can_browse_the_library()
    {
        await using var factory = new ApiFactory();
        var client = factory.CreateClient();

        var list = await client.GetFromJsonAsync<List<LibrarySummaryResponse>>("/api/library");
        Assert.True(list!.Count >= 50);
        Assert.Contains(list, r => r.Slug == "aip-chicken-adobo" && r.Tags.Contains("AIP") && r.Tags.Contains("Asian"));

        var detail = await client.GetFromJsonAsync<LibraryDetailResponse>("/api/library/aip-chicken-adobo");
        Assert.Equal("AIP Chicken Adobo", detail!.Title);
        Assert.Equal(Enumerable.Range(1, detail.Steps.Count), detail.Steps.Select(s => s.Order));
        Assert.Contains(detail.Ingredients, i => i.Name == "Coconut Aminos" && i.StoreSection == "Condiments & Sauces");

        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync("/api/library/not-a-recipe")).StatusCode);
    }

    [Fact]
    public async Task Adding_requires_login()
    {
        await using var factory = new ApiFactory();
        var response = await factory.CreateClient().PostAsync("/api/library/chicken-adobo/add", null);
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Adding_copies_the_recipe_into_my_book()
    {
        await using var factory = new ApiFactory();
        using (var scope = factory.Services.CreateScope())
            await DbSeeder.SeedAsync(scope.ServiceProvider.GetRequiredService<AppDbContext>(),
                scope.ServiceProvider.GetRequiredService<Microsoft.AspNetCore.Hosting.IWebHostEnvironment>().ContentRootPath);

        var client = await factory.CreateUserClientAsync();
        var library = await client.GetFromJsonAsync<LibraryDetailResponse>("/api/library/beef-and-broccoli");

        var add = await client.PostAsync("/api/library/beef-and-broccoli/add", null);
        Assert.Equal(HttpStatusCode.Created, add.StatusCode);
        var id = (await add.Content.ReadFromJsonAsync<LibraryAddResponse>())!.RecipeId;

        var mine = await client.GetFromJsonAsync<JsonElement>($"/api/recipes/{id}");
        Assert.Equal("Beef and Broccoli", mine.GetProperty("title").GetString());
        Assert.Equal(library!.Ingredients.Count, mine.GetProperty("ingredients").GetArrayLength());
        Assert.Equal(library.Steps.Count, mine.GetProperty("steps").GetArrayLength());
        var techniques = mine.GetProperty("techniques").EnumerateArray().Select(t => t.GetProperty("name").GetString()).ToList();
        Assert.Contains("Searing", techniques);
        Assert.Contains("Blanching", techniques);
        Assert.Contains(mine.GetProperty("ingredients").EnumerateArray(),
            i => i.GetProperty("name").GetString() == "Flank Steak" && i.GetProperty("quantity").GetDecimal() == 1m);

        // Adding again makes a second, independent copy; another user can't see either
        var again = await client.PostAsync("/api/library/beef-and-broccoli/add", null);
        Assert.Equal(HttpStatusCode.Created, again.StatusCode);
        var other = await factory.CreateUserClientAsync();
        Assert.Equal(HttpStatusCode.NotFound, (await other.GetAsync($"/api/recipes/{id}")).StatusCode);
    }

    [Fact]
    public async Task Adding_works_even_on_an_unseeded_database()
    {
        await using var factory = new ApiFactory(); // no seeding: ingredients get created, techniques skipped
        var client = await factory.CreateUserClientAsync();

        var add = await client.PostAsync("/api/library/aip-guacamole/add", null);
        Assert.Equal(HttpStatusCode.Created, add.StatusCode);
        var id = (await add.Content.ReadFromJsonAsync<LibraryAddResponse>())!.RecipeId;

        var mine = await client.GetFromJsonAsync<JsonElement>($"/api/recipes/{id}");
        Assert.Contains(mine.GetProperty("ingredients").EnumerateArray(),
            i => i.GetProperty("name").GetString() == "Avocado" && i.GetProperty("storeSection").GetString() == "Produce");

        Assert.Equal(HttpStatusCode.NotFound, (await client.PostAsync("/api/library/nope/add", null)).StatusCode);
    }
}
