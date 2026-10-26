using System.Diagnostics;
using System.Net;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using OpenTelemetry.Instrumentation.AspNetCore;
using OpenTelemetry.Instrumentation.Http;
using RecipePlanner.Api.Monitoring;

namespace RecipePlanner.Tests;

public class MonitoringTests
{
    private sealed class MonitoredApi : ApiFactory
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            // A real-looking connection string pointing nowhere: telemetry is set up but never reaches Azure
            builder.UseSetting("APPLICATIONINSIGHTS_CONNECTION_STRING",
                "InstrumentationKey=00000000-0000-0000-0000-000000000000;IngestionEndpoint=https://127.0.0.1:1/");
            base.ConfigureWebHost(builder);
        }
    }

    [Fact]
    public async Task Health_check_is_public_and_reports_the_database()
    {
        using var factory = new ApiFactory();
        var response = await factory.CreateClient().GetAsync("/api/health");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("Healthy", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task With_application_insights_on_the_app_still_runs_and_skips_health_pings()
    {
        using var factory = new MonitoredApi();
        Assert.Equal(HttpStatusCode.OK, (await factory.CreateClient().GetAsync("/api/health")).StatusCode);

        var options = factory.Services.GetRequiredService<IOptionsMonitor<AspNetCoreTraceInstrumentationOptions>>().Get(Options.DefaultName);
        Assert.NotNull(options.Filter);
        Assert.False(options.Filter!(new DefaultHttpContext { Request = { Path = "/api/health" } }));
        Assert.True(options.Filter(new DefaultHttpContext { Request = { Path = "/api/recipes" } }));
    }

    [Fact]
    public void Outgoing_calls_lose_their_query_string_so_keys_never_reach_telemetry()
    {
        using var factory = new MonitoredApi();
        var options = factory.Services.GetRequiredService<IOptionsMonitor<HttpClientTraceInstrumentationOptions>>().Get(Options.DefaultName);
        using var activity = new Activity("test").Start();
        activity.SetTag("url.full", "https://api.nal.usda.gov/fdc/v1/foods/search?api_key=SECRET");
        activity.SetTag("url.query", "api_key=SECRET");

        options.EnrichWithHttpRequestMessage!(activity, new HttpRequestMessage(HttpMethod.Post, "https://api.nal.usda.gov/fdc/v1/foods/search?api_key=SECRET"));

        Assert.Equal("https://api.nal.usda.gov/fdc/v1/foods/search", activity.GetTagItem("url.full"));
        Assert.Null(activity.GetTagItem("url.query"));
        Assert.DoesNotContain(activity.TagObjects, t => t.Value?.ToString()?.Contains("SECRET") == true);
    }

    [Fact]
    public void Incoming_requests_lose_their_query_string()
    {
        using var activity = new Activity("test").Start();
        activity.SetTag("url.query", "code=ONE-TIME&state=abc");
        MonitoringExtensions.DropQuery(activity);
        Assert.Null(activity.GetTagItem("url.query"));
    }
}
