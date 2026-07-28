# Subscription billing (Maxio Advanced Billing)

An **additive, parallel** capability on top of eShopOnWeb's one-time commerce flow: a logged-in
shopper can browse recurring plans, subscribe to one, and see it reflected in their account.
**Maxio Advanced Billing is the system of record** — there is no local subscription table; state is
read back from Maxio on every request, keyed by a stable per-user reference.

## Endpoints (JWT-authenticated, under `/api/`)

| Method & route | Purpose | Auth |
|---|---|---|
| `GET /api/subscription-plans` | List the plans in the configured product family (handle, name, price, interval). | Any authenticated user |
| `POST /api/subscriptions` | Subscribe the caller to a plan (`{ "planHandle": "eshop-pro" }`). Idempotent. | Caller identity from JWT |
| `GET /api/my-subscriptions` | List the caller's own subscriptions (plan, price, state, next-billing date). | Caller identity from JWT |

The caller's identity **always** comes from the JWT (`ClaimTypes.Name`), never from the request body.
`POST /api/subscriptions` returns **201 Created** for a new enrollment and **200 OK** with
`alreadySubscribed: true` for an idempotent replay.

## Configuration

Settings bind from the `Maxio` configuration section (POCO: `Infrastructure/Maxio/MaxioSettings.cs`):

| Key | Source env var | Required | Notes |
|---|---|---|---|
| `Maxio:ApiKey` | `MAXIO_API_KEY` | yes | HTTP Basic username (password is the literal `"x"`). |
| `Maxio:Subdomain` | `MAXIO_SITE_SUBDOMAIN` | yes | Site subdomain → `https://{subdomain}.chargify.com`. |
| `Maxio:ProductFamilyHandle` | `MAXIO_DEFAULT_PRODUCT_FAMILY` | yes | Product family whose products are offered as plans. |
| `Maxio:BaseUrl` | — | no | Optional base-URL override; used verbatim when set (e.g. a mock host). |

**Secrets never live in the repo.** Load them into .NET user-secrets on the `PublicApi` project:

```bash
cd src/PublicApi
dotnet user-secrets set "Maxio:ApiKey"              "$MAXIO_API_KEY"
dotnet user-secrets set "Maxio:Subdomain"           "$MAXIO_SITE_SUBDOMAIN"
dotnet user-secrets set "Maxio:ProductFamilyHandle" "$MAXIO_DEFAULT_PRODUCT_FAMILY"
```

Configuration is validated lazily (on the first subscription call), so the rest of the API still boots
when billing is unconfigured; a subscription call then fails clearly (HTTP 503) naming the missing keys.

## Idempotency & concurrency (production-grade design)

Maxio exposes no atomic upsert / idempotency key, so idempotency is **check-then-create** against a
deterministic reference, hardened against double-clicks and true races:

- **Customer** — the eShop username is used as the Maxio customer `reference`; ensure = read-by-reference,
  create only on a 404. A concurrent duplicate-create (422) is reconciled by re-reading the winner.
- **Subscription** — before creating, the customer's subscriptions are listed and any non-terminal one to
  the same plan is reused. Subscribes are billed on invoice/remittance so **no card capture is required**.
- **Serialization** — a keyed per-user async lock serializes concurrent subscribes for the same user, so a
  6-way concurrent burst yields exactly one customer and one subscription.
- **Transport safety** — because the SDK retries transport failures on POSTs too, a failed write is
  reconciled by re-reading before surfacing an error.

All Maxio SDK usage is confined to `src/Infrastructure/Maxio/`; ApplicationCore and PublicApi stay
SDK-agnostic (they depend only on `IMaxioBillingService` and the `ApplicationCore.Billing` domain types).
