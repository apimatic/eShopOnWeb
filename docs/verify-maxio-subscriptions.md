# Verifying the Maxio subscription integration

Step-by-step check that the subscribe flow really works end to end. Roughly five minutes.
Design notes are in [maxio-subscriptions.md](./maxio-subscriptions.md).

## 0. Prerequisites

- The .NET SDK. `global.json` pins 8.0.x with `rollForward: latestMajor`, so a newer SDK is fine;
  run with `DOTNET_ROLL_FORWARD=Major` if your machine still complains.
- The ASP.NET Core 8.0 runtime, or the same roll-forward.
- A trusted HTTPS dev certificate — both hosts call `UseHttpsRedirection()`:

  ```pwsh
  dotnet dev-certs https --check --trust
  ```

- `MAXIO_API_KEY`, `MAXIO_SITE_SUBDOMAIN` and `MAXIO_DEFAULT_PRODUCT_FAMILY` in your environment.

No SQL Server, Docker, broker or other infrastructure is needed.

## 1. Load the credentials into user-secrets

Secrets never enter the repository — this reads them from the environment and stores them outside it:

```pwsh
pwsh ./scripts/set-maxio-user-secrets.ps1
```

Expect `Maxio:Subdomain`, `Maxio:ProductFamilyHandle` and `Maxio:ApiKey` to be listed back
(the key is redacted in the output).

## 2. Build and run the tests

```pwsh
dotnet build eShopOnWeb.sln
dotnet test eShopOnWeb.sln
```

All four test projects should pass. The billing tests under
`tests/UnitTests/Infrastructure/Billing` cover the idempotency rules against a stubbed Maxio API
and hit no network.

## 3. Start the PublicApi host

LocalDB is not required — run against the in-memory provider:

```pwsh
$env:DOTNET_ROLL_FORWARD = 'Major'
$env:ASPNETCORE_ENVIRONMENT = 'Development'
$env:UseOnlyInMemoryDatabase = 'true'
dotnet run --project src/PublicApi/PublicApi.csproj
```

It listens on `https://localhost:26723` (and `http://localhost:26724`). Swagger, including the
three new endpoints under the **SubscriptionEndpoints** tag, is at
<https://localhost:26723/swagger>.

Stop any earlier instance first so you are not testing a stale build.

## 4. Get a bearer token

PublicApi uses JWT — the storefront cookie will not work here.

```pwsh
$api = 'https://localhost:26723'
$auth = Invoke-RestMethod "$api/api/authenticate" -Method Post -ContentType 'application/json' `
    -Body '{"username":"demouser@microsoft.com","password":"Pass@word1"}'
$headers = @{ Authorization = "Bearer $($auth.token)" }
```

## 5. Browse the plans

```pwsh
(Invoke-RestMethod "$api/api/subscription-plans" -Headers $headers).subscriptionPlans |
    Format-Table handle, name, price, currency, intervalUnit, requiresPaymentMethod
```

Expect the plans from your product family, cheapest first — for the demo catalog, `basic-plan`
at 29.00 USD/month and `eshop-pro` at 299.00 USD/month, both with `requiresPaymentMethod: False`.
These come from Maxio by **handle**, so they stay correct after a catalog re-seed.

## 6. Subscribe — the hero flow

```pwsh
$sub = Invoke-RestMethod "$api/api/subscriptions" -Method Post -Headers $headers `
    -ContentType 'application/json' -Body '{"planHandle":"eshop-pro"}'
$sub | ConvertTo-Json -Depth 4
```

Expect `alreadySubscribed: false`, a `customerId`, a `customerReference` of
`eshoponweb-demouser@microsoft.com`, and a subscription with `state: active`, `price: 299`,
`currency: USD` and a `nextBillingAt` one month out. The HTTP status is **201 Created**.

## 7. Prove the double-click is safe

Fire two subscribes at once for the same plan:

```pwsh
$job = 1..2 | ForEach-Object {
    Start-ThreadJob -ArgumentList $api, $headers {
        param($api, $headers)
        Invoke-WebRequest "$api/api/subscriptions" -Method Post -Headers $headers `
            -ContentType 'application/json' -Body '{"planHandle":"eshop-pro"}'
    }
}
$job | Receive-Job -Wait | ForEach-Object {
    $body = $_.Content | ConvertFrom-Json
    "{0}  id={1}  alreadySubscribed={2}" -f $_.StatusCode, $body.subscription.id, $body.alreadySubscribed
}
```

If step 6 was the shopper's first ever subscribe, expect one **201 … alreadySubscribed=False** and
one **200 … alreadySubscribed=True**; once they are already enrolled both come back **200 …
alreadySubscribed=True**. Either way both responses carry the **same subscription id**. Repeat the
single call as often as you like — it stays 200 with that id, and Maxio ends up with exactly one
customer and one subscription.

To watch the 201/200 pair from a genuinely cold start, run the same block with a token for
`admin@microsoft.com` (same password), who has not been enrolled yet.

With `curl` instead:

```bash
TOKEN=$(curl -sk -X POST https://localhost:26723/api/authenticate -H 'Content-Type: application/json' \
  -d '{"username":"demouser@microsoft.com","password":"Pass@word1"}' | jq -r .token)

curl -sk -X POST https://localhost:26723/api/subscriptions -H "Authorization: Bearer $TOKEN" \
  -H 'Content-Type: application/json' -d '{"planHandle":"eshop-pro"}' -w '\nHTTP %{http_code}\n'
```

## 8. See it on the account

```pwsh
$mine = Invoke-RestMethod "$api/api/my-subscriptions" -Headers $headers
$mine.activeCount
$mine.subscriptions | Format-Table id, state, planHandle, price, currency, nextBillingAt
```

Subscribe to `basic-plan` as well and it appears as a second, separate subscription — one live
enrollment *per plan*.

## 9. Confirm it in Maxio itself

Either open the site in the Maxio UI, or read the API directly:

```bash
curl -sS -u "$MAXIO_API_KEY:X" \
  "https://$MAXIO_SITE_SUBDOMAIN.chargify.com/customers/lookup.json?reference=eshoponweb-demouser@microsoft.com"

curl -sS -u "$MAXIO_API_KEY:X" \
  "https://$MAXIO_SITE_SUBDOMAIN.chargify.com/customers/<id-from-above>/subscriptions.json"
```

Exactly one customer with that reference, and one subscription per plan, each with
`payment_collection_method: remittance` and a reference of the form
`eshoponweb-demouser@microsoft.com:eshop-pro`.

## 10. Restart and confirm nothing was lost

Stop the host and start it again (step 3). The in-memory database is wiped and identity GUIDs are
regenerated, yet:

```pwsh
(Invoke-RestMethod "$api/api/my-subscriptions" -Headers $headers).subscriptions |
    Format-Table id, state, planHandle
```

still shows the same subscriptions, and subscribing again returns **200** with
`alreadySubscribed: true`. Maxio is the system of record, keyed by a reference derived from the
stable user name — nothing depends on local persistence.

## 11. Error paths worth a glance

| Request | Expected |
|---|---|
| Any of the three endpoints with no bearer token | `401` |
| `POST /api/subscriptions` with `{}` | `400`, `planHandle is required.` |
| `POST /api/subscriptions` with `{"planHandle":"does-not-exist"}` | `400`, *Unknown subscription plan* |
| Any endpoint with `Maxio:ApiKey` unset | `503` naming the missing setting; the rest of the API keeps working |
