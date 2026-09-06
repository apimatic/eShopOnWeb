# Verifying the Maxio subscription billing integration

About five minutes end to end. Every command is copy-pasteable from the repository root.

> Design notes and configuration reference live in
> [maxio-subscription-billing.md](maxio-subscription-billing.md).

---

## 1. Load the sandbox credentials into user secrets

They are read from the environment and written outside the repository. Skip this if you have already
run it — `dotnet user-secrets list --project src/PublicApi` will show the three keys.

```bash
dotnet user-secrets set "Maxio:ApiKey"               "$MAXIO_API_KEY"                --project src/PublicApi
dotnet user-secrets set "Maxio:Subdomain"            "$MAXIO_SITE_SUBDOMAIN"         --project src/PublicApi
dotnet user-secrets set "Maxio:ProductFamilyHandle"  "$MAXIO_DEFAULT_PRODUCT_FAMILY" --project src/PublicApi
```

## 2. Build and run the tests

```bash
dotnet build eShopOnWeb.sln
dotnet test  eShopOnWeb.sln
```

Expect 176 passing tests across five assemblies. `MaxioBillingTests` contributes 102, including the
suite that re-checks this integration against `maxio-spec/openapi.yaml` and read-only smoke calls
against the live sandbox.

## 3. Start the API

`global.json` pins the SDK to 8.0.x and rolls forward, so the .NET 10 SDK builds it and the
ASP.NET Core 8 runtime hosts it. `UseOnlyInMemoryDatabase=true` avoids the LocalDB dependency.

```bash
DOTNET_ROLL_FORWARD=Major \
ASPNETCORE_ENVIRONMENT=Development \
ASPNETCORE_URLS="https://localhost:26023;http://localhost:26024" \
UseOnlyInMemoryDatabase=true \
dotnet run --project src/PublicApi --no-launch-profile
```

Startup logs one line that confirms how billing is wired, with no credential in it:

```
info: Maxio[0]
      Subscription billing targets https://cp-exp-1.chargify.com/ using product family
      'eshop-subscribe' and collection method 'remittance'.
```

Leave it running and use a second shell for the rest. (`curl -k` below skips dev-cert validation;
`dotnet dev-certs https --check --trust` removes the need for it.)

## 4. Get a bearer token

The storefront cookie does not work here — PublicApi is JWT only.

```bash
API=https://localhost:26023
TOKEN=$(curl -sk -X POST "$API/api/authenticate" \
  -H "Content-Type: application/json" \
  -d '{"username":"demouser@microsoft.com","password":"Pass@word1"}' \
  | python -c "import json,sys; print(json.load(sys.stdin)['token'])")
```

## 5. Browse the plans

```bash
curl -sk "$API/api/subscription-plans" -H "Authorization: Bearer $TOKEN"
```

Both seeded plans come back, priced from the live sandbox — `basic-plan` at $29.00/month and
`eshop-pro` at $299.00/month, currency read from the Maxio site:

```json
{"plans":[
  {"handle":"basic-plan","name":"Basic Plan","priceInCents":2900,"price":29,"currency":"USD",
   "billingPeriod":"month","requiresPaymentMethod":false,"productFamilyHandle":"eshop-subscribe"},
  {"handle":"eshop-pro","name":"Pro Plan","priceInCents":29900,"price":299,"currency":"USD",
   "billingPeriod":"month","requiresPaymentMethod":false,"productFamilyHandle":"eshop-subscribe"}]}
```

## 6. Subscribe — the hero flow

```bash
curl -sk -X POST "$API/api/subscriptions" \
  -H "Authorization: Bearer $TOKEN" -H "Content-Type: application/json" \
  -d '{"planHandle":"eshop-pro"}' -w "\nHTTP %{http_code}\n"
```

`HTTP 201`, with `created: true`, `customerCreated: true`, and the plan, price, `state: "active"`
and `nextBillingAt` one month out.

## 7. See it in the account

```bash
curl -sk "$API/api/my-subscriptions" -H "Authorization: Bearer $TOKEN"
```

One subscription, `activeCount: 1`.

## 8. Double-click: repeat the exact same subscribe

```bash
curl -sk -X POST "$API/api/subscriptions" \
  -H "Authorization: Bearer $TOKEN" -H "Content-Type: application/json" \
  -d '{"planHandle":"eshop-pro"}' -w "\nHTTP %{http_code}\n"
```

`HTTP 200` this time, `created: false`, `customerCreated: false`, and the **same** subscription
`id`. Re-run step 7 to confirm there is still exactly one.

## 9. Six of them at once

The real double-click is concurrent, so fire six in parallel. Use the admin account, which has not
subscribed yet:

```bash
ATOKEN=$(curl -sk -X POST "$API/api/authenticate" \
  -H "Content-Type: application/json" \
  -d '{"username":"admin@microsoft.com","password":"Pass@word1"}' \
  | python -c "import json,sys; print(json.load(sys.stdin)['token'])")

for i in 1 2 3 4 5 6; do
  curl -sk -X POST "$API/api/subscriptions" \
    -H "Authorization: Bearer $ATOKEN" -H "Content-Type: application/json" \
    -d '{"planHandle":"basic-plan"}' -w " HTTP %{http_code}\n" -o /dev/null &
done; wait
```

Exactly one `201`; the other five are `200`. Confirm with
`curl -sk "$API/api/my-subscriptions" -H "Authorization: Bearer $ATOKEN"` — one subscription.

## 10. Confirm it in Maxio itself

Same records, straight from the provider — this is what "system of record" means here:

```bash
curl -su "$MAXIO_API_KEY:x" \
  "https://$MAXIO_SITE_SUBDOMAIN.chargify.com/customers/lookup.json?reference=eshoponweb-demouser-microsoft-com-03563e80"
```

Or open <https://cp-exp-1.chargify.com> and look for the customers whose Reference starts with
`eshoponweb-`.

## 11. Error paths

```bash
# Unknown plan -> 404
curl -sk -X POST "$API/api/subscriptions" -H "Authorization: Bearer $TOKEN" \
  -H "Content-Type: application/json" -d '{"planHandle":"no-such-plan"}' -w "\nHTTP %{http_code}\n"

# No plan handle -> 400
curl -sk -X POST "$API/api/subscriptions" -H "Authorization: Bearer $TOKEN" \
  -H "Content-Type: application/json" -d '{}' -w "\nHTTP %{http_code}\n"

# No token -> 401
curl -sk "$API/api/subscription-plans" -w "\nHTTP %{http_code}\n"
```

## 12. Nothing is hard-coded to this site

Stop the API, then start it pointed elsewhere with the `Maxio:BaseUrl` override and note the startup
line changes to match. `Maxio__BaseUrl` is the environment-variable spelling of `Maxio:BaseUrl`:

```bash
Maxio__BaseUrl=https://cp-exp-1.chargify.com \
DOTNET_ROLL_FORWARD=Major ASPNETCORE_ENVIRONMENT=Development \
ASPNETCORE_URLS="http://localhost:26026" UseOnlyInMemoryDatabase=true \
dotnet run --project src/PublicApi --no-launch-profile
```

And with billing deliberately unconfigured, the host still starts, the catalog still serves, and
only the subscription routes answer `503` naming the missing setting:

```bash
Maxio__Subdomain= \
DOTNET_ROLL_FORWARD=Major ASPNETCORE_ENVIRONMENT=Development \
ASPNETCORE_URLS="http://localhost:26026" UseOnlyInMemoryDatabase=true \
dotnet run --project src/PublicApi --no-launch-profile
# GET /api/subscription-plans -> 503 "'Maxio:Subdomain' is required unless 'Maxio:BaseUrl' is set."
# GET /api/catalog-brands     -> 200
```

## 13. Swagger

<https://localhost:26023/swagger> lists the three routes under **SubscriptionEndpoints**. Click
**Authorize**, paste `Bearer <token>` from step 4, and drive the flow from the browser.

---

### Cleaning up

Stop the API with Ctrl-C, or:

```bash
netstat -ano | grep LISTENING | grep ':2602[34]'   # find the pid
```

Subscriptions created during verification stay in the sandbox by design — Maxio is the system of
record and eShopOnWeb never deletes from it. Cancel them from the Maxio UI if you want a clean slate
before re-running; step 6 will then create a fresh subscription with a `-2` suffixed reference.
