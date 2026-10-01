using Microsoft.EntityFrameworkCore;
using RecipePlanner.Api.Data;
using RecipePlanner.Api.Endpoints;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddDbContext<AppDbContext>(o =>
    o.UseNpgsql(builder.Configuration.GetConnectionString("Default")));
builder.Services.AddOpenApi();

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.UseHttpsRedirection();
app.MapRecipeEndpoints();
app.MapTechniqueEndpoints();
app.Run();

public partial class Program { }