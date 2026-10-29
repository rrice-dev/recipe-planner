<p align="center"><img src="docs/logo.svg" alt="Thyme Capsule logo" width="140"></p>

# Thyme Capsule

![CI](https://github.com/rrice-dev/thyme-capsule/actions/workflows/ci.yml/badge.svg)

Recipes worth keeping. A technique-focused recipe and meal planning app that can read a photo of a cookbook page or a handwritten family recipe card and turn it into a recipe you can cook from.

**Stack:** .NET 10 (ASP.NET Core, EF Core) · PostgreSQL · Angular · Docker · GitHub Actions

## Run locally

1. Copy `.env.example` to `.env` and set a password
2. `docker compose up --build`
3. Open http://localhost:8080
## Deployment notes

- **Keep the API's ingress internal.** The API trusts `X-Forwarded-For`/`X-Forwarded-Host` from any sender because only the web container's nginx can reach it (see `Program.cs`). Making the API's ingress external would let clients fake their IP and dodge rate limits.

## Monitoring

In Azure, the API reports to Application Insights, an uptime test checks `/api/health` every 5 minutes, and alerts send an email when the site is down or failing. Setup and what is (and isn't) collected: [docs/monitoring.md](docs/monitoring.md).
