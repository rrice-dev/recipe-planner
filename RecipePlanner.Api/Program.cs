using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using RecipePlanner.Api.Ai;
using RecipePlanner.Api.Data;
using RecipePlanner.Api.Email;
using RecipePlanner.Api.Nutrition;
using RecipePlanner.Api.Kroger;
using RecipePlanner.Api.Library;
using RecipePlanner.Api.Endpoints;
using RecipePlanner.Api.Models;
using RecipePlanner.Api.Monitoring;
using RecipePlanner.Api.Photos;
using RecipePlanner.Api.Security;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddDbContext<AppDbContext>(o =>
    o.UseNpgsql(builder.Configuration.GetConnectionString("Default")));

// "Admin" policy: only users in the Admin role can change shared data
// Login cookies are encrypted with Data Protection keys. By default those keys live inside the
// container and vanish when it restarts, logging everyone out. Keeping them in the database fixes that.
// Set up Key Vault (docs/key-encryption.md) so the keys aren't stored in plain text; see Security/KeyEncryption.cs
builder.Services.AddDataProtection()
    .PersistKeysToDbContext<AppDbContext>()
    .SetApplicationName("RecipePlanner")
    .ProtectKeysIfConfigured(builder.Configuration);

builder.Services.AddAuthorizationBuilder()
    .AddPolicy(Roles.Admin, p => p.RequireRole(Roles.Admin));

builder.Services.AddIdentityApiEndpoints<AppUser>(o =>
{
    // Length over complexity, following current NIST guidance
    o.Password.RequiredLength = 12;
    o.Password.RequireDigit = false;
    o.Password.RequireUppercase = false;
    o.Password.RequireLowercase = false;
    o.Password.RequireNonAlphanumeric = false;

    // Slow down brute-force attempts
    o.Lockout.MaxFailedAccessAttempts = 5;
    o.Lockout.DefaultLockoutTimeSpan = TimeSpan.FromMinutes(15);
    o.Lockout.AllowedForNewUsers = true;

    o.User.RequireUniqueEmail = true;
})
.AddRoles<IdentityRole>() // Enables roles; must come before the stores line
.AddEntityFrameworkStores<AppDbContext>();

builder.Services.ConfigureApplicationCookie(o =>
{
    o.Cookie.Name = "rp_session";
    o.Cookie.HttpOnly = true;                              // JavaScript can't read it
    o.Cookie.SameSite = SameSiteMode.Strict;               // Not sent on cross-site requests (CSRF defense)
    o.Cookie.SecurePolicy = CookieSecurePolicy.SameAsRequest; // Secure flag whenever served over HTTPS
    o.ExpireTimeSpan = TimeSpan.FromDays(7);
    o.SlidingExpiration = true;

    // An API should return status codes, not redirect to a login page
    o.Events.OnRedirectToLogin = ctx =>
    {
        ctx.Response.StatusCode = StatusCodes.Status401Unauthorized;
        return Task.CompletedTask;
    };
    o.Events.OnRedirectToAccessDenied = ctx =>
    {
        ctx.Response.StatusCode = StatusCodes.Status403Forbidden;
        return Task.CompletedTask;
    };
});

builder.Services.AddAppRateLimiting();
builder.Services.AddMealIdeas(builder.Configuration);
builder.Services.AddRecipePhotos(builder.Configuration);
builder.Services.AddKroger(builder.Configuration);
builder.Services.AddAppEmail(builder.Configuration);
builder.Services.AddNutrition(builder.Configuration);
builder.Services.AddRecipeLibrary(builder.Environment.ContentRootPath);
builder.Services.AddMemoryCache();
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddSingleton<RecipePlanner.Api.Import.IPageFetcher, RecipePlanner.Api.Import.SafePageFetcher>();

// Requests arrive through two proxies: Azure's ingress -> nginx -> Azure's internal ingress -> API.
// nginx overwrites X-Forwarded-For with the client IP Azure reported, so the API can trust
// up to two hops. Without this, every user would share nginx's IP and one rate-limit bucket.
builder.Services.Configure<ForwardedHeadersOptions>(o =>
{
    // Host too, so share previews can build full https://<your site>/... picture addresses
    o.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto | ForwardedHeaders.XForwardedHost;
    o.ForwardLimit = 2;
    // Trusting every sender is safe ONLY because the API has internal-only ingress: nothing but our own
    // proxies can reach it. If the API's ingress is ever made external, anyone could fake X-Forwarded-For
    // (dodging rate limits) and X-Forwarded-Host. Then list the proxies' addresses here instead of clearing.
    o.KnownIPNetworks.Clear();
    o.KnownProxies.Clear();
});

builder.Services.AddOpenApi();
builder.AddAppMonitoring();

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

    if (app.Configuration.GetValue<bool>("Database:MigrateOnStartup"))
        await db.Database.MigrateAsync();

    if (app.Environment.IsDevelopment() || app.Configuration.GetValue<bool>("Database:SeedOnStartup"))
        await DbSeeder.SeedAsync(db, app.Environment.ContentRootPath);

    var startupLog = app.Services.GetRequiredService<ILoggerFactory>().CreateLogger("Startup");
    if (!string.IsNullOrWhiteSpace(app.Configuration[KeyEncryption.SettingName]))
    {
        var deleted = await KeyEncryption.DeleteUnencryptedKeysAsync(db);
        if (deleted > 0)
            startupLog.LogWarning("Deleted {Count} unencrypted Data Protection key(s); everyone will need to sign in again", deleted);
    }
    else if (app.Environment.IsProduction())
    {
        startupLog.LogWarning("Data Protection keys are stored unencrypted. Set {Setting} (see docs/key-encryption.md).",
            KeyEncryption.SettingName);
    }

    // Creates the Admin role and promotes the account named in Admin:Email, if set
    await AdminSeeder.EnsureAdminAsync(scope.ServiceProvider, app.Configuration["Admin:Email"]);
}

app.UseForwardedHeaders(); // First, so everything after sees the real client IP and scheme
app.UseHttpsRedirection();
app.UseAuthentication();
app.UseRateLimiter(); // After authentication, so limits can be per user
app.UseAuthorization();

app.MapAppHealth();
app.MapAuthEndpoints();
app.MapRecipeEndpoints();
app.MapTechniqueEndpoints();
app.MapIngredientEndpoints();
app.MapMealPlanEndpoints();
app.MapFridgeEndpoints();
app.MapSharingEndpoints();
app.MapRecipePhotoEndpoints();
app.MapSourcePageEndpoints();
app.MapGroceryListEndpoints();
app.MapKrogerEndpoints();
app.MapLibraryEndpoints();
app.MapWorldEndpoints();
app.MapSettingsEndpoints();
app.MapPersonalEndpoints();
app.MapImportEndpoints();
app.MapNutritionEndpoints();
app.MapPasswordResetEndpoints();
app.MapKitchenEndpoints();
app.MapCostEndpoints();

app.Run();

public partial class Program { }