using System.Net;
using System.Net.Http.Json;
using Microsoft.Extensions.DependencyInjection;
using RecipePlanner.Api.Ai;
using RecipePlanner.Api.Contracts;
using RecipePlanner.Api.Data;
using RecipePlanner.Api.Import;

namespace RecipePlanner.Tests;

public class IngredientLineParserTests
{
    [Theory]
    [InlineData("2 cups all-purpose flour", 2, "cup", "All-purpose flour", null)]
    [InlineData("1 1/2 tablespoons olive oil", 1.5, "tbsp", "Olive oil", null)]
    [InlineData("1½ tsp kosher salt", 1.5, "tsp", "Kosher salt", null)]
    [InlineData("½ cup milk", 0.5, "cup", "Milk", null)]
    [InlineData("3 cloves garlic, minced", 3, "clove", "Garlic", "minced")]
    [InlineData("1 (14 oz) can diced tomatoes", 1, "can", "Diced tomatoes", "14 oz")]
    [InlineData("2-3 pounds chicken thighs (bone-in)", 3, "lb", "Chicken thighs", "bone-in")]
    [InlineData("4 large eggs", 4, "whole", "Eggs", null)]
    [InlineData("1 onion, finely chopped", 1, "whole", "Onion", "finely chopped")]
    [InlineData("200 g dark chocolate", 200, "g", "Dark chocolate", null)]
    [InlineData("8 ounces of cream cheese, softened", 8, "oz", "Cream cheese", "softened")]
    [InlineData("1&nbsp;cup sugar", 1, "cup", "Sugar", null)]
    public void Parses_common_ingredient_lines(string line, double qty, string unit, string name, string? note)
    {
        var p = IngredientLineParser.Parse(line);
        Assert.Equal((decimal)qty, p.Quantity);
        Assert.Equal(unit, p.Unit);
        Assert.Equal(name, p.Name);
        Assert.Equal(note, p.Note);
    }

    [Fact]
    public void Lines_without_an_amount_are_kept_with_a_note()
    {
        var p = IngredientLineParser.Parse("Salt and pepper to taste");
        Assert.Equal((1m, "whole", "Salt and pepper to taste", "amount not given"), (p.Quantity, p.Unit, p.Name, p.Note));
    }
}

public class RecipeJsonLdTests
{
    private static string Page(string json) =>
        $"<html><head><script type=\"application/ld+json\">{{\"@type\":\"WebSite\",\"name\":\"x\"}}</script>" +
        $"<script type='application/ld+json'>{json}</script></head><body>hi</body></html>";

    [Fact]
    public void Reads_a_recipe_inside_a_graph_with_sections_and_durations()
    {
        var html = Page("""
        {"@context":"https://schema.org","@graph":[
          {"@type":"WebPage","name":"Page"},
          {"@type":["Recipe","NewsArticle"],"name":"Lemon &amp; Herb Chicken","description":"<p>Bright and <b>easy</b>.</p>",
           "recipeYield":["4","4 servings"],"prepTime":"PT15M","totalTime":"PT1H5M","recipeCuisine":["Greek"],
           "recipeIngredient":["2 lb chicken thighs","1 lemon, juiced"],
           "recipeInstructions":[
             {"@type":"HowToSection","name":"Marinade","itemListElement":[{"@type":"HowToStep","text":"Mix the lemon and herbs."}]},
             {"@type":"HowToStep","text":"Roast at 425°F for 40 minutes."}]}
        ]}
        """);
        var r = RecipeJsonLd.Find(html)!;
        Assert.Equal("Lemon & Herb Chicken", r.Title);
        Assert.Equal("Bright and easy .", r.Description!.Replace("  ", " "));
        Assert.Equal(4, r.Servings);
        Assert.Equal(15, r.PrepMinutes);
        Assert.Equal(50, r.CookMinutes); // total minus prep
        Assert.Equal("Greek", r.Cuisine);
        Assert.Equal(["Mix the lemon and herbs.", "Roast at 425°F for 40 minutes."], r.Steps);
        Assert.Equal(2, r.IngredientLines.Count);
    }

    [Fact]
    public void Reads_instructions_given_as_one_block_of_text()
    {
        var r = RecipeJsonLd.Find(Page("""{"@type":"Recipe","name":"Toast","recipeYield":"2 slices","recipeIngredient":["2 slices bread"],"recipeInstructions":"Toast the bread.<br>Butter it."}"""))!;
        Assert.Equal(["Toast the bread.", "Butter it."], r.Steps);
        Assert.Equal(2, r.Servings);
    }

    [Fact]
    public void Ignores_pages_without_a_recipe_and_broken_json()
    {
        Assert.Null(RecipeJsonLd.Find("<html><body>No recipe here</body></html>"));
        Assert.Null(RecipeJsonLd.Find(Page("{ not json")));
        Assert.Null(RecipeJsonLd.Find(Page("""{"@type":"Article","name":"News"}""")));
    }

    [Theory]
    [InlineData("PT1H30M", 90)]
    [InlineData("PT45M", 45)]
    [InlineData("P0DT0H20M", 20)]
    [InlineData("nonsense", null)]
    public void Reads_iso_durations(string iso, int? minutes) => Assert.Equal(minutes, RecipeJsonLd.Minutes(iso));
}

public class SafePageFetcherTests
{
    [Theory]
    [InlineData("8.8.8.8", true)]
    [InlineData("151.101.1.67", true)]
    [InlineData("2606:4700:4700::1111", true)]
    [InlineData("127.0.0.1", false)]
    [InlineData("10.1.2.3", false)]
    [InlineData("172.16.0.5", false)]
    [InlineData("192.168.1.10", false)]
    [InlineData("169.254.169.254", false)]   // Azure/AWS metadata service
    [InlineData("100.100.1.1", false)]       // carrier-grade NAT, used inside some clouds
    [InlineData("0.0.0.0", false)]
    [InlineData("224.0.0.1", false)]
    [InlineData("::1", false)]
    [InlineData("::ffff:10.0.0.1", false)]   // IPv4 private address written as IPv6
    [InlineData("fd00::1", false)]
    [InlineData("fe80::1", false)]
    [InlineData("64:ff9b::a00:1", false)]    // NAT64 route to 10.0.0.1
    public void Only_public_addresses_are_allowed(string ip, bool allowed) =>
        Assert.Equal(allowed, SafePageFetcher.IsPublic(IPAddress.Parse(ip)));

    [Theory]
    [InlineData("ftp://example.com/recipe")]
    [InlineData("file:///etc/passwd")]
    [InlineData("http://user:pass@example.com/")]
    [InlineData("http://example.com:8080/")]
    [InlineData("http://169.254.169.254/metadata/identity")]
    [InlineData("http://[::1]/")]
    [InlineData("http://localhost/")]
    [InlineData("http://recipe-api.internal/")]
    [InlineData("http://intranet/")]
    public void Rejects_unsafe_links_before_connecting(string url) =>
        Assert.Throws<ImportException>(() => SafePageFetcher.CheckUrl(new Uri(url)));

    [Fact]
    public void Allows_ordinary_recipe_links() =>
        SafePageFetcher.CheckUrl(new Uri("https://www.example.com/recipes/lemon-chicken?print=1"));
}

public class FakePages(string html) : IPageFetcher
{
    public int Calls { get; private set; }
    public Task<FetchedPage> FetchAsync(Uri url, CancellationToken ct)
    {
        Calls++;
        return Task.FromResult(new FetchedPage(url, html));
    }
}

public class FakeExtractor(RawRecipe? recipe) : IRecipeExtractor
{
    public string? LastText { get; private set; }
    public bool IsAvailable => true;
    public Task<RawRecipe?> ExtractAsync(string pageText, CancellationToken ct) { LastText = pageText; return Task.FromResult(recipe); }
}

public class ImportEndpointTests
{
    private const string RecipePage = """
        <html><head><script type="application/ld+json">
        {"@type":"Recipe","name":"Weeknight Chili","recipeYield":"6","prepTime":"PT10M","cookTime":"PT40M",
         "recipeIngredient":["2 lb ground beef","1 (28 oz) can crushed tomatoes","2 tbsp chili powder","Salt to taste"],
         "recipeInstructions":[{"@type":"HowToStep","text":"Brown the beef in a large pot."},{"@type":"HowToStep","text":"Add everything else and simmer 40 minutes."}]}
        </script></head><body></body></html>
        """;

    private static async Task<HttpClient> Client(ApiFactory factory)
    {
        using (var scope = factory.Services.CreateScope())
            await DbSeeder.SeedAsync(scope.ServiceProvider.GetRequiredService<AppDbContext>(),
                scope.ServiceProvider.GetRequiredService<Microsoft.AspNetCore.Hosting.IWebHostEnvironment>().ContentRootPath);
        return await factory.CreateUserClientAsync();
    }

    [Fact]
    public async Task Imports_from_the_pages_recipe_data_and_links_techniques()
    {
        using var factory = new ApiFactory { Pages = new FakePages(RecipePage), Extractor = new FakeExtractor(null) };
        var client = await Client(factory);

        var response = await client.PostAsJsonAsync("/api/import", new ImportRequest("https://example.com/chili"));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var result = await response.Content.ReadFromJsonAsync<ImportResponse>();
        Assert.Equal("structured", result!.Method);
        Assert.Equal("https://example.com/chili", result.SourceUrl);
        var r = result.Recipe;
        Assert.Equal(("Weeknight Chili", 6, 10, 40), (r.Title, r.Servings, r.PrepMinutes, r.CookMinutes));
        Assert.Contains(r.Ingredients, i => i.Name == "Crushed tomatoes" && i.Unit == "can" && i.Note == "28 oz");
        Assert.Contains(r.Ingredients, i => i.Name == "Salt to taste" && i.Note == "amount not given");
        Assert.Equal("Simmering", r.Steps[1].TechniqueName);
    }

    [Fact]
    public async Task Falls_back_to_the_AI_when_the_page_has_no_recipe_data()
    {
        var extractor = new FakeExtractor(new RawRecipe("Grandma's Soup", "Simple.", 4, 10, 30,
            [new("Carrots", 2, "whole", null)], [new("Simmer everything for 30 minutes.", null)]));
        var html = "<html><head><script>var tracking=1;</script><style>body{}</style></head><body><nav>Menu</nav><h1>Grandma's Soup</h1><p>Chop carrots.</p></body></html>";
        using var factory = new ApiFactory { Pages = new FakePages(html), Extractor = extractor };
        var client = await Client(factory);

        var result = await (await client.PostAsJsonAsync("/api/import", new ImportRequest("https://blog.example.com/soup"))).Content.ReadFromJsonAsync<ImportResponse>();
        Assert.Equal("ai", result!.Method);
        Assert.Equal("Grandma's Soup", result.Recipe.Title);
        // The AI only sees readable text: no scripts, styles or menus
        Assert.Contains("Chop carrots.", extractor.LastText);
        Assert.DoesNotContain("tracking", extractor.LastText);
        Assert.DoesNotContain("Menu", extractor.LastText);
    }

    [Fact]
    public async Task Explains_when_there_is_no_recipe()
    {
        using var factory = new ApiFactory { Pages = new FakePages("<html><body>Just news</body></html>"), Extractor = new FakeExtractor(null) };
        var client = await Client(factory);
        var response = await client.PostAsJsonAsync("/api/import", new ImportRequest("https://example.com/news"));
        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
        Assert.Contains("Couldn't find a recipe", await response.Content.ReadAsStringAsync());
    }

    [Theory]
    [InlineData("http://169.254.169.254/metadata/instance")]
    [InlineData("http://localhost:5099/api/settings")]
    [InlineData("javascript:alert(1)")]
    [InlineData("not a link")]
    public async Task Refuses_unsafe_links_without_fetching(string url)
    {
        var pages = new FakePages(RecipePage);
        using var factory = new ApiFactory { Pages = pages };
        var client = await Client(factory);
        var response = await client.PostAsJsonAsync("/api/import", new ImportRequest(url));
        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
        Assert.Equal(0, pages.Calls);
    }

    [Fact]
    public async Task Requires_login()
    {
        using var factory = new ApiFactory { Pages = new FakePages(RecipePage) };
        var response = await factory.CreateClient().PostAsJsonAsync("/api/import", new ImportRequest("https://example.com/chili"));
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Saved_recipes_keep_their_source_and_only_web_links_are_allowed()
    {
        using var factory = new ApiFactory();
        var client = await factory.CreateUserClientAsync();
        var created = await (await client.PostAsJsonAsync("/api/recipes",
            new RecipeRequest("Chili", null, null, 6, 10, 40, "https://example.com/chili"))).Content.ReadFromJsonAsync<RecipeResponse>();
        Assert.Equal("https://example.com/chili", (await client.GetFromJsonAsync<RecipeDetailResponse>($"/api/recipes/{created!.Id}"))!.SourceUrl);

        // Editing later without a source keeps it
        await client.PutAsJsonAsync($"/api/recipes/{created.Id}", new RecipeRequest("Chili!", null, null, 6, 10, 40));
        Assert.Equal("https://example.com/chili", (await client.GetFromJsonAsync<RecipeDetailResponse>($"/api/recipes/{created.Id}"))!.SourceUrl);

        foreach (var bad in new[] { "javascript:alert(1)", "data:text/html,hi", "ftp://x.com/a" })
        {
            var response = await client.PostAsJsonAsync("/api/recipes", new RecipeRequest("X", null, null, 1, 1, 1, bad));
            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        }
    }
}

public class SafePageFetcherNetworkTests
{
    // A public-looking hostname that resolves to this machine (DNS rebinding). Only runs where the test
    // host maps rebind-test.example to 127.0.0.1 (add it to /etc/hosts); elsewhere it has nothing to check.
    [Fact]
    public async Task Refuses_hostnames_that_resolve_to_private_addresses()
    {
        IPAddress[] resolved;
        try { resolved = await Dns.GetHostAddressesAsync("rebind-test.example"); }
        catch (System.Net.Sockets.SocketException) { return; }
        if (!resolved.All(IPAddress.IsLoopback)) return;

        var ex = await Assert.ThrowsAsync<ImportException>(() =>
            new SafePageFetcher().FetchAsync(new Uri("http://rebind-test.example/"), CancellationToken.None));
        Assert.Contains("private address", ex.Message);
    }
}
