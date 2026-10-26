using System.Diagnostics;
using Azure.Monitor.OpenTelemetry.AspNetCore;
using Npgsql;
using OpenTelemetry.Instrumentation.AspNetCore;
using OpenTelemetry.Instrumentation.Http;
using OpenTelemetry.Logs;
using RecipePlanner.Api.Data;

namespace RecipePlanner.Api.Monitoring;

// Health check for uptime monitoring, plus Azure Application Insights when a connection string is set
// (APPLICATIONINSIGHTS_CONNECTION_STRING). Without one, nothing is sent anywhere.
public static class MonitoringExtensions
{
    public const string HealthPath = "/api/health";

    public static WebApplicationBuilder AddAppMonitoring(this WebApplicationBuilder builder)
    {
        // "Can the API reach its database?" Used by Azure's availability test.
        builder.Services.AddHealthChecks().AddDbContextCheck<AppDbContext>("database");

        var connectionString = builder.Configuration["APPLICATIONINSIGHTS_CONNECTION_STRING"];
        if (string.IsNullOrWhiteSpace(connectionString)) return builder;

        builder.Services.AddOpenTelemetry()
            .UseAzureMonitor(o => o.ConnectionString = connectionString)
            .WithTracing(t => t.AddNpgsql());   // database calls show up as dependencies, with timings

        builder.Services.Configure<AspNetCoreTraceInstrumentationOptions>(o =>
        {
            // Uptime pings every few minutes would be most of the data and say nothing new
            o.Filter = http => !http.Request.Path.StartsWithSegments(HealthPath);
            o.EnrichWithHttpRequest = (activity, _) => DropQuery(activity);
        });
        builder.Services.Configure<HttpClientTraceInstrumentationOptions>(o =>
            o.EnrichWithHttpRequestMessage = (activity, request) => DropQuery(activity, request.RequestUri));

        // Only what's worth paying to keep: app logs, and warnings from the framework.
        // EF Core logs every SQL command at Information, which would be most of the bill.
        builder.Logging.AddFilter<OpenTelemetryLoggerProvider>("Microsoft", LogLevel.Warning);
        builder.Logging.AddFilter<OpenTelemetryLoggerProvider>("System", LogLevel.Warning);
        return builder;
    }

    public static IEndpointRouteBuilder MapAppHealth(this IEndpointRouteBuilder app)
    {
        app.MapHealthChecks(HealthPath).AllowAnonymous().DisableRateLimiting();
        return app;
    }

    /// <summary>
    /// Query strings can carry secrets (the USDA key is sent as ?api_key=, Kroger's sign-in sends back a
    /// one-time code). They're dropped before anything leaves for Application Insights.
    /// </summary>
    public static void DropQuery(Activity activity, Uri? outgoing = null)
    {
        activity.SetTag("url.query", null);
        if (outgoing is not null) activity.SetTag("url.full", outgoing.GetLeftPart(UriPartial.Path));
    }
}
