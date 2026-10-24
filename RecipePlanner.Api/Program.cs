using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using RecipePlanner.Api.Data;
using RecipePlanner.Api.Endpoints;
using RecipePlanner.Api.Models;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddDbContext<AppDbContext>(o =>
    o.UseNpgsql(builder.Configuration.GetConnectionString("Default")));

// "Admin" policy: only users in the Admin role can change shared data
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

builder.Services.AddOpenApi();

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

    // Creates the Admin role and promotes the account named in Admin:Email, if set
    await AdminSeeder.EnsureAdminAsync(scope.ServiceProvider, app.Configuration["Admin:Email"]);
}

app.UseHttpsRedirection();
app.UseAuthentication();
app.UseAuthorization();

app.MapAuthEndpoints();
app.MapRecipeEndpoints();
app.MapTechniqueEndpoints();
app.MapIngredientEndpoints();
app.MapMealPlanEndpoints();

app.Run();

public partial class Program { }