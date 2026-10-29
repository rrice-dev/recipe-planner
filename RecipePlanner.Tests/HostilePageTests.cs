using System.Diagnostics;
using System.Net;
using System.Net.Http.Json;
using RecipePlanner.Api.Contracts;
using RecipePlanner.Api.Import;

namespace RecipePlanner.Tests;

// Pages that make the HTML-cleaning regexes slow down quadratically (unclosed tags). Before the fix, a
// 200 KB page of "<script>" took about a minute of CPU, and pages up to 3 MB are allowed. Every one of
// these must come back quickly, as an import that fails politely.
public class HostilePageTests
{
    // As big as the importer accepts, minus room for the wrapper some cases need
    private const int Kb = (SafePageFetcher.MaxBytes - 1024) / 1024;

    private static string Repeat(string unit, int kilobytes) => string.Concat(Enumerable.Repeat(unit, kilobytes * 1024 / unit.Length));

    public static TheoryData<string, string> Pages => new()
    {
        { "unclosed script tags", Repeat("<script>", Kb) },
        { "unclosed comments", Repeat("<!--", Kb) },
        { "unclosed brackets", Repeat("<", Kb) },
        { "unclosed recipe data tags", Repeat("<script type=\"application/ld+json\">", Kb) },
        { "an ingredient line of open brackets",
          "<script type=\"application/ld+json\">{\"@type\":\"Recipe\",\"name\":\"Trap\",\"recipeIngredient\":[\"" + Repeat("(", Kb) + "\"]}</script>" },
    };

    [Theory]
    [MemberData(nameof(Pages))]
    public async Task A_hostile_page_fails_quickly_instead_of_tying_up_the_server(string what, string html)
    {
        using var factory = new ApiFactory { Pages = new FakePages(html), Extractor = new FakeExtractor(null) };
        var client = await factory.CreateUserClientAsync();

        var clock = Stopwatch.StartNew();
        var request = client.PostAsJsonAsync("/api/import", new ImportRequest("https://example.com/trap"));
        var finished = await Task.WhenAny(request, Task.Delay(TimeSpan.FromSeconds(20)));

        Assert.True(finished == request, $"{what}: still running after 20 seconds");
        var response = await request;
        Assert.True(clock.Elapsed < TimeSpan.FromSeconds(20), $"{what}: took {clock.Elapsed.TotalSeconds:N1} s");
        Assert.True(response.StatusCode is HttpStatusCode.OK or HttpStatusCode.UnprocessableEntity,
            $"{what}: got {(int)response.StatusCode}");
    }

    [Fact]
    public void Ordinary_pages_are_unaffected()
    {
        var html = "<html><head><style>p{}</style></head><body><!-- note --><nav>Menu</nav><h1>Soup</h1><p>Chop<br>Boil</p>"
                   + "<script>var x = 1;</script></body></html>";
        Assert.Equal("Soup\nChop\nBoil", PageText.From(html));
    }
}
