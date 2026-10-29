# Monitoring

The API sends requests, database and outside-service calls, exceptions and its own log messages to
**Azure Application Insights** when the `APPLICATIONINSIGHTS_CONNECTION_STRING` setting is present.
Without it, nothing is sent anywhere. An **availability test** can check `/api/health` from outside
(it's set up but turned **off**, to save money; see below), and **alerts** email you when the API starts
failing.

## What's collected, and what isn't

| Collected | Left out |
|---|---|
| Each API request: route, status code, duration | Query strings (they can hold one-time codes) |
| Database queries (timing, and the SQL text with no values) | Request and response bodies |
| Calls to Kroger, USDA, Anthropic and Azure email (timing, status) | Query strings on those calls (the USDA key is sent in one) |
| Exceptions and the app's own warnings and log lines | Framework chatter below Warning level |
| | `/api/health` pings (the availability test records those itself) |

Code: `RecipePlanner.Api/Monitoring/MonitoringExtensions.cs`. Tests: `MonitoringTests.cs`.

## One-time setup

Run these one at a time. They use the existing Log Analytics workspace that Container Apps already
writes to, so there's one place for all logs, and its free monthly allowance covers a small site.

```bash
RG=recipe-planner-rg
SITE=https://<your recipe-web address>
az extension add --name application-insights
```

Create Application Insights in the same workspace:

```bash
WS_CUSTOMER=$(az containerapp env show -g $RG -n $(az containerapp env list -g $RG --query "[0].name" -o tsv) --query properties.appLogsConfiguration.logAnalyticsConfiguration.customerId -o tsv)
WS_ID=$(az monitor log-analytics workspace list --query "[?customerId=='$WS_CUSTOMER'].id" -o tsv)
az monitor app-insights component create --app recipe-insights -g $RG -l centralus --workspace "$WS_ID" --kind web --application-type web --output none
AI_ID=$(az monitor app-insights component show --app recipe-insights -g $RG --query id -o tsv)
```

Give the API the connection string as a secret (this restarts the API on a new revision):

```bash
CS=$(az monitor app-insights component show --app recipe-insights -g $RG --query connectionString -o tsv)
az containerapp secret set -n recipe-api -g $RG --secrets appinsights-cs="$CS" --output none
az containerapp update -n recipe-api -g $RG --set-env-vars APPLICATIONINSIGHTS_CONNECTION_STRING=secretref:appinsights-cs --output none
unset CS
```

Check the health endpoint answers through the site:

```bash
curl -s $SITE/api/health
```

It should print `Healthy`.

### Availability test

```bash
az monitor app-insights web-test create --name recipe-health --defined-web-test-name recipe-health --synthetic-monitor-id recipe-health -g $RG -l centralus --web-test-kind standard --tags "hidden-link:$AI_ID=Resource" --locations Id=us-il-ch1-azr --frequency 900 --timeout 30 --retry-enabled true --enabled true --http-verb GET --request-url "$SITE/api/health" --expected-status-code 200 --ssl-check true --output none
```

The first run can take up to a minute while the API wakes from scale-to-zero, which is why the
timeout is 30 seconds and retries are on.

**It's turned off.** Each check is billed, and each one wakes both apps, which then stay on for a few
minutes, so even every 15 minutes it kept the apps running about a third of the time. With it off, the
apps only run when someone uses the site, and "API errors" still emails you about failures then. To
turn it back on, or off again:

```bash
az monitor app-insights web-test update --name recipe-health -g $RG --enabled true --output none   # or false
```

**Why one region every 15 minutes, when it's on:** each check is billed (Standard Web Test Execution), and each one
wakes both apps, which then stay on for a few minutes. Three regions every 5 minutes was 864 checks a
day, about $0.40, and kept the apps from ever scaling to zero, so it was most of the Azure bill. One
region every 15 minutes is 96 checks a day, and the apps can sleep between them. The trade-off: an
outage is noticed within about 15 minutes instead of 5.

### Email alerts

```bash
az monitor action-group create -g $RG -n recipe-alerts --short-name recipealert --action email me <your email> --output none
az monitor metrics alert create -n "Site is down" -g $RG --scopes "$AI_ID" --condition "avg availabilityResults/availabilityPercentage < 100" --window-size 15m --evaluation-frequency 5m --severity 1 --action recipe-alerts --description "The uptime check of /api/health failed" --output none
az monitor metrics alert create -n "API errors" -g $RG --scopes "$AI_ID" --condition "count requests/failed > 5" --window-size 15m --evaluation-frequency 5m --severity 2 --action recipe-alerts --description "More than 5 failed API requests in 15 minutes" --output none
```

"Site is down" only fires while the availability test is on; with it off, it stays quiet and costs nothing.

## Looking at it

In the Azure portal, open **recipe-insights**:

- **Application map**: the API and everything it calls, with failure rates.
- **Failures** and **Performance**: slowest and failing routes, with the full chain of calls for one request.
- **Availability**: the uptime test results.
- **Logs**: queries like these.

```kusto
// Slowest routes this week
requests
| where timestamp > ago(7d)
| summarize count(), p95_ms = percentile(duration, 95) by name
| order by p95_ms desc

// Anything that went wrong in the last day
union exceptions, (traces | where severityLevel >= 2)
| where timestamp > ago(1d)
| project timestamp, type, message, outerMessage
| order by timestamp desc

// Did emails go out? (logged with Azure's operation id, never the address)
traces
| where message has "Email" and message has "finished with status"
| project timestamp, message
```
