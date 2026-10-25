using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using RecipePlanner.Api.Ai;
using RecipePlanner.Api.Data;

namespace RecipePlanner.Tests;

public class ApiFactory : WebApplicationFactory<Program>
{
    // Two factories with the same name share one database, which is how tests simulate an API restart
    public string DatabaseName { get; init; } = Guid.NewGuid().ToString();

    // Set this to a fake to test the AI feature without calling a real provider
    public IMealIdeaGenerator? MealIdeas { get; init; }
    public IIngredientScanner? Scanner { get; init; }
    public IRecipeDraftGenerator? RecipeDrafts { get; init; }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<DbContextOptions<AppDbContext>>();
            services.RemoveAll<IDbContextOptionsConfiguration<AppDbContext>>();
            services.AddDbContext<AppDbContext>(o => o.UseInMemoryDatabase(DatabaseName));

            if (MealIdeas is not null)
            {
                services.RemoveAll<IMealIdeaGenerator>();
                services.AddSingleton(MealIdeas);
            }

            if (RecipeDrafts is not null)
            {
                services.RemoveAll<IRecipeDraftGenerator>();
                services.AddSingleton(RecipeDrafts);
            }

            if (Scanner is not null)
            {
                services.RemoveAll<IIngredientScanner>();
                services.AddSingleton(Scanner);
            }
        });
    }
}