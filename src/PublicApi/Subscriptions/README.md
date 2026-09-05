# Maxio subscriptions

The PublicApi exposes JWT-protected subscription routes:

- `GET /api/subscription-plans`
- `POST /api/subscriptions` with `{ "planHandle": "..." }`
- `GET /api/my-subscriptions`

`Maxio:ApiKey`, `Maxio:Subdomain`, and `Maxio:ProductFamilyHandle` belong in the
PublicApi user-secrets store. `Maxio:BaseUrl` is optional; when absent the adapter
uses `https://{Maxio:Subdomain}.chargify.com/`.

The adapter resolves the product family by handle, never by a seeded numeric ID.
It uses deterministic customer and subscription references in Maxio for retry-safe
customer creation and enrollment. Subscription creation uses Maxio's `remittance`
collection method so the seeded card-optional plans do not collect card details.
