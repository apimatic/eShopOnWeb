# API Endpoints

This folder demonstrates how to configure API endpoints as individual classes. You can compare it to the traditional controller-based approach found in /Web/Controllers/Api.

## Subscription billing (Maxio Advanced Billing)

Recurring subscriptions run alongside the one-time Catalog/Basket/Order flow. Maxio Advanced Billing is the
system of record. All three endpoints require a PublicApi JWT (`POST api/authenticate`); the shopper is
identified only by the token's name claim.

| Endpoint | Purpose | Success |
| --- | --- | --- |
| `GET /api/subscription-plans` | Non-archived plans of the configured product family: `handle`, `name`, `description`, `priceInCents`, `formattedPrice`, `interval`, `intervalUnit`. `truncated: true` means the list hit the page cap and may be incomplete. | 200 |
| `POST /api/subscriptions` with `{ "planHandle": "<handle>" }` | Ensures a Maxio customer exists for the shopper, then subscribes it to the plan. Idempotent per shopper and plan. Returns the subscription (id, reference, plan, price, currency, state, `nextBillingAt`, period end, created/activated, `paymentCollectionMethod`) and `created`. Subscriptions are created with `payment_collection_method: remittance` (invoice-billed), because this flow captures no card and Maxio refuses automatic collection without a payment method on file. | 201 new, 200 already subscribed |
| `GET /api/my-subscriptions` | The shopper's subscriptions. | 200 |

Errors (JSON `{ statusCode, message, errors?, status?, reference? }`):

| Status | Meaning |
| --- | --- |
| 400 | `planHandle` missing or not one of the listed plans |
| 422 | Maxio refused the request; `errors` carries Maxio's messages |
| 202 | `status: "pending_confirmation"`: the write was sent but its outcome is not confirmed yet. Repeat the same request; it settles the outcome before doing anything else. Never a failure. |
| 409 | `status: "in_progress"`: an identical request is running right now |
| 502 | Maxio unavailable, credentials/configuration rejected, or an unreadable response |
| 503 | Maxio throttling, or the plan list was too large to validate the plan |
| 504 | Maxio did not answer within the 30 s request budget |

How duplicates are prevented: before each Maxio write the API inserts a claim row (`BillingCustomerClaims` /
`BillingSubscriptionClaims` in `CatalogContext`) whose primary key is a deterministic, PII-free reference
(`eshop-user-<hash>` per shopper, `eshop-sub-<hash>` per shopper and plan). A second concurrent request is refused
by the database. The same references are sent to Maxio and are used to re-read Maxio whenever a write's outcome is
unknown (connection failure, timeout, unreadable response). With `UseOnlyInMemoryDatabase=true` the claims are lost
on restart; the pre-write lookups by reference (customer lookup, subscription lookup plus a scan of the customer's
subscriptions for the same reference) still prevent duplicates across restarts.

### Configuration

Settings bind from the `Maxio` section. The host refuses to start if a required key is missing or blank.

| Key | Required | Meaning |
| --- | --- | --- |
| `Maxio:ApiKey` | yes | Maxio API key (sent as HTTP Basic username; the password is `x`) |
| `Maxio:Subdomain` | unless `Maxio:BaseUrl` is set | Site subdomain; the base address becomes `https://<subdomain>.chargify.com` |
| `Maxio:ProductFamilyHandle` | yes | Handle of the product family whose products are the plans |
| `Maxio:BaseUrl` | no | Used verbatim as the API base address instead of deriving it from the subdomain |

Never commit values. For local development use user-secrets (this project already has a `UserSecretsId`):

```bash
dotnet user-secrets set "Maxio:ApiKey" "<your-api-key>" --project src/PublicApi
dotnet user-secrets set "Maxio:Subdomain" "<your-site-subdomain>" --project src/PublicApi
dotnet user-secrets set "Maxio:ProductFamilyHandle" "<your-product-family-handle>" --project src/PublicApi
```

Or environment variables: `Maxio__ApiKey`, `Maxio__Subdomain`, `Maxio__ProductFamilyHandle`, `Maxio__BaseUrl`.
The API key is read once at startup; a rotated key takes effect after a restart.

### The Maxio SDK reference is machine-local

The Maxio Advanced Billing .NET SDK is not on NuGet. It ships as source inside the `maxio-advanced-billing`
Claude Code plugin, and `PublicApi.csproj` references it where it sits through the MSBuild property
`MaxioSdkProjectPath`. Its default points into the plugin install directory next to this repository
(`..\..\..\marketplace\plugins\maxio-advanced-billing\sdk\dotnet\src\MaxioAdvancedBilling\MaxioAdvancedBilling.csproj`).
On another machine or a CI runner, point it at that machine's copy:

```bash
dotnet build eShopOnWeb.sln -p:MaxioSdkProjectPath=/path/to/sdk/dotnet/src/MaxioAdvancedBilling/MaxioAdvancedBilling.csproj
```

The build fails with a clear message when the path does not exist. The PublicApi `Dockerfile` build context does not
contain the SDK, so container builds need the SDK copied into the context and the property pointed at it. Do not copy
the SDK source into this repository.
