# Maxio subscription billing (eShopOnWeb)

An **additive**, parallel capability alongside the existing one-time commerce flow: a logged-in shopper
browses recurring plans, subscribes, and sees their subscriptions. **Maxio Advanced Billing** is the system
of record — there is no local subscription table, so the flow survives the in-memory database resets.

## What was added

| Layer | Files |
|-------|-------|
| Domain contract + DTOs | `src/ApplicationCore/Interfaces/ISubscriptionBillingService.cs`, `src/ApplicationCore/Subscriptions/*` |
| Maxio SDK integration | `src/Infrastructure/Maxio/*` (`MaxioSettings`, `MaxioSubscriptionBillingService`, `MaxioServiceCollectionExtensions`) |
| HTTP endpoints (JWT) | `src/PublicApi/SubscriptionEndpoints/*`, wired in `src/PublicApi/Program.cs` |
| Tests | `tests/IntegrationTests/Maxio/MaxioSubscriptionBillingServiceTests.cs` |
| Vendored SDK | `libs/MaxioAdvancedBilling.1.0.0.nupkg` + `nuget.config` (the SDK is source-only, not on NuGet) |

Endpoints (all JWT-authenticated; caller identity comes from the token's name claim):

- `GET  /api/subscription-plans` — the plans (products in the configured Maxio product family).
- `POST /api/subscriptions` — body `{ "planHandle": "eshop-pro" }`; ensures a Maxio customer (idempotent)
  and subscribes. Idempotent overall: a live subscription to the same plan is returned rather than
  duplicated (a double-click never creates two customers/subscriptions).
- `GET  /api/my-subscriptions` — the caller's subscriptions.

## Configuration

Settings bind from the `Maxio:` section — `Maxio:ApiKey`, `Maxio:Subdomain`, `Maxio:ProductFamilyHandle`,
and the optional `Maxio:BaseUrl` (used verbatim when set; otherwise the base URL is derived from the
subdomain). **No secret values are committed.** Startup fails fast if any of the first three are blank.

Load the sandbox credentials into user-secrets (values from the provided env vars — run from the repo root):

```bash
dotnet user-secrets set "Maxio:ApiKey"              "$MAXIO_API_KEY"              --project src/PublicApi
dotnet user-secrets set "Maxio:Subdomain"           "$MAXIO_SITE_SUBDOMAIN"       --project src/PublicApi
dotnet user-secrets set "Maxio:ProductFamilyHandle" "$MAXIO_DEFAULT_PRODUCT_FAMILY" --project src/PublicApi
```

## Verify it yourself

This machine has only the .NET 10/11 SDK (no ASP.NET Core 8 runtime) and no LocalDB, so:

```bash
export DOTNET_ROLL_FORWARD=Major
```

1. **Build & unit tests**

   ```bash
   dotnet build eShopOnWeb.sln
   dotnet test tests/IntegrationTests/IntegrationTests.csproj --filter MaxioSubscriptionBillingServiceTests
   ```

2. **Run PublicApi** (in-memory DB; assigned ports 30623/30624):

   ```bash
   UseOnlyInMemoryDatabase=true ASPNETCORE_ENVIRONMENT=Development \
   ASPNETCORE_URLS="https://localhost:30623;http://localhost:30624" \
   dotnet run --project src/PublicApi --no-launch-profile
   ```

3. **Exercise the hero flow** (JWT from the authenticate endpoint; `-k` because of the HTTPS dev cert):

   ```bash
   TOKEN=$(curl -sk -X POST https://localhost:30623/api/authenticate \
     -H "Content-Type: application/json" \
     -d '{"username":"demouser@microsoft.com","password":"Pass@word1"}' \
     | python -c "import sys,json;print(json.load(sys.stdin)['token'])")

   curl -sk https://localhost:30623/api/subscription-plans -H "Authorization: Bearer $TOKEN"
   curl -sk -X POST https://localhost:30623/api/subscriptions \
     -H "Authorization: Bearer $TOKEN" -H "Content-Type: application/json" \
     -d '{"planHandle":"eshop-pro"}'
   curl -sk https://localhost:30623/api/my-subscriptions -H "Authorization: Bearer $TOKEN"
   ```

   Expected: plans list `eshop-pro`/`basic-plan` with prices; the subscribe call returns the confirmed
   plan/price/`state`/`nextBillingDate` (`alreadySubscribed:true` if the user already has that plan);
   my-subscriptions lists it. Subscribing twice returns the **same** `subscriptionId` and never creates a
   duplicate.

Interactive docs: `https://localhost:30623/swagger` (endpoints under the **SubscriptionEndpoints** tag).

All Maxio interaction goes exclusively through the **maxio-platforms-team** plugin's .NET SDK; the design
and contract sheet are in [`maxio-advanced-billing-plan.md`](maxio-advanced-billing-plan.md).
