# Stage 1: build with the full .NET SDK
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

# Restore first, so Docker can cache packages between builds
COPY RecipePlanner.Api.csproj .
RUN dotnet restore

COPY . .
RUN dotnet publish -c Release -o /app/publish --no-restore

# Stage 2: run with the small runtime-only image
FROM mcr.microsoft.com/dotnet/aspnet:10.0
WORKDIR /app
COPY --from=build /app/publish .

# Run as a non-root user built into the .NET image
USER $APP_UID

EXPOSE 8080
ENTRYPOINT ["dotnet", "RecipePlanner.Api.dll"]