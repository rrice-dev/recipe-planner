# Recipe Planner

![CI](https://github.com/rrice-dev/recipe-planner/actions/workflows/ci.yml/badge.svg)

A technique-focused recipe and meal planning app.

**Stack:** .NET 10 (ASP.NET Core, EF Core) · PostgreSQL · Angular · Docker · GitHub Actions

## Run locally

1. Copy `.env.example` to `.env` and set a password
2. `docker compose up --build`
3. Open http://localhost:8080
## Monitoring

In Azure, the API reports to Application Insights, an uptime test checks `/api/health` every 5 minutes, and alerts send an email when the site is down or failing. Setup and what is (and isn't) collected: [docs/monitoring.md](docs/monitoring.md).
