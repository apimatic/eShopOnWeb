# API Endpoints

This folder demonstrates how to configure API endpoints as individual classes. You can compare it to the traditional controller-based approach found in /Web/Controllers/Api.

## Subscription billing (Maxio Advanced Billing)

Recurring subscriptions run alongside the existing one-time Catalog → Basket → Order flow; they do
not replace it. **Maxio is the system of record** — no subscription state is mirrored into the
eShopOnWeb database.

| Endpoint | Purpose |
| --- | --- |
| `GET /api/subscription-plans` | Plans published on the configured Maxio product family, cheapest first |
| `POST /api/subscriptions` | Subscribe the caller to a plan (`{"planHandle": "..."}`) |
| `GET /api/my-subscriptions` | The caller's subscriptions, live from Maxio |

All three require a JWT from `POST /api/authenticate`. The subscriber is taken from the token's name
claim — the caller never states who they are.

### Configuration

Bound from the `Maxio:` configuration section. Supply values through user-secrets in development or
the platform secret store in production; never commit them.

| Key | Required | Notes |
| --- | --- | --- |
| `Maxio:ApiKey` | yes | Site API key. Sent as the HTTP Basic username with `X` as the password |
| `Maxio:Subdomain` | yes* | Site subdomain; the base address becomes `https://{subdomain}.chargify.com/` |
| `Maxio:BaseUrl` | no | Verbatim base address override. When set it wins over `Maxio:Subdomain` |
| `Maxio:ProductFamilyHandle` | yes | Handle of the family whose products are offered as plans |
| `Maxio:ReferencePrefix` | no | Namespace for the references this app owns (default `eshoponweb`) |
| `Maxio:PaymentCollectionMethod` | no | Default `remittance`; eShopOnWeb captures no card details |

\* Either `Maxio:Subdomain` or `Maxio:BaseUrl` must be set.

Nothing about a particular Maxio site or catalog is compiled in: plans and the product family are
addressed by **handle**, never by numeric id, because Maxio reassigns ids when a site is re-seeded.

With no Maxio configuration the host still starts normally and the three endpoints answer
`503 Service Unavailable` — an unconfigured integration must not take the rest of the API down.

```bash
dotnet user-secrets set "Maxio:ApiKey"              "$MAXIO_API_KEY"              --project src/PublicApi
dotnet user-secrets set "Maxio:Subdomain"           "$MAXIO_SITE_SUBDOMAIN"       --project src/PublicApi
dotnet user-secrets set "Maxio:ProductFamilyHandle" "$MAXIO_DEFAULT_PRODUCT_FAMILY" --project src/PublicApi
```

### Idempotency

Subscribing twice must never enroll a shopper twice or charge them twice. Three layers, none of
which depend on the eShopOnWeb database — so the guarantee survives a restart even when running on
the in-memory provider:

1. **Deterministic references.** The Maxio customer reference is a pure function of the shopper's
   login (`eshoponweb-demouser-microsoft-com-<hash>`), and the subscription reference a pure
   function of that plus the plan handle. Maxio enforces both unique. A repeat request looks the
   record up and adopts it instead of creating another.
2. **A per-shopper lock** serialises concurrent subscribe attempts inside one process, so the common
   double-click never even reaches Maxio twice.
3. **A per-attempt `uniqueness_token`** makes a single `POST /subscriptions` safe for the HTTP layer
   to replay after a timeout or 5xx. It is random per attempt rather than derived from the
   reference: Maxio remembers a token for 60 minutes whether or not the request succeeded, so a
   derived token would lock a shopper out for an hour after a failed try.

A first-time enrollment answers `201`; a replay answers `200` with `"created": false`. Subscribing
to the same plan again after an earlier subscription has been canceled or expired starts a new one.

### Failure mapping

| Situation | Status |
| --- | --- |
| No/invalid bearer token | `401` |
| `planHandle` missing | `400` (with the available handles) |
| Plan handle not published | `404` |
| An identical submission is still in flight upstream | `409` |
| Maxio not configured on this host | `503` |
| Maxio rejected us or was unreachable | `502` |

Reads and replay-safe writes are retried with exponential backoff and jitter on `429`/`5xx`/network
errors, honouring `Retry-After`. Writes without a uniqueness token are never replayed.
