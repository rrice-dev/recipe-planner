<p align="center"><img src="docs/logo.svg" alt="Thyme Capsule logo" width="140"></p>

# Thyme Capsule

![CI](https://github.com/rrice-dev/thyme-capsule/actions/workflows/ci.yml/badge.svg)

Recipes worth keeping. A technique-focused recipe and meal planning app that can read a photo of a cookbook page or a handwritten family recipe card and turn it into a recipe you can cook from.

**Stack:** .NET 10 (ASP.NET Core, EF Core) · PostgreSQL · Angular · Docker · GitHub Actions

## Run locally

1. Copy `.env.example` to `.env` and set a password
2. `docker compose up --build`
3. Open http://localhost:8080

## Tests

- **API:** `dotnet test`. Runs on real Postgres: with Docker running, a throwaway `postgres:16` container starts automatically. To use a server you already have, set `TEST_POSTGRES` to its connection string instead (test databases are named `rptest_*`, and old ones are dropped at the start of each run).
- **Web:** `npx ng test --watch=false` in `recipe-planner-web`.

## Monitoring

In Azure, the API reports to Application Insights, an uptime test checks `/api/health` every 5 minutes, and alerts send an email when the site is down or failing. Setup and what is (and isn't) collected: [docs/monitoring.md](docs/monitoring.md).
