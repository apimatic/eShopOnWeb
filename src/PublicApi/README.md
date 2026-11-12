# API Endpoints

This folder demonstrates how to configure API endpoints as individual classes. You can compare it to the traditional controller-based approach found in /Web/Controllers/Api.


## Subscriptions (Maxio Advanced Billing)

JWT-authenticated endpoints, additive to the catalog/basket/order flow:

- `GET /api/subscription-plans` - plans offered by the configured Maxio product family
- `POST /api/subscriptions` - body `{ "planHandle": "..." }`; subscribes the token's user (idempotent per user and plan: `201` first time, `200` afterwards, `409` while another request for the same user/plan is in flight)
- `GET /api/my-subscriptions` - the user's subscriptions with plan, price, state and next billing date

Configuration (section `Maxio`, no defaults; startup fails if a required value is missing):
`Maxio:ApiKey`, `Maxio:Subdomain`, `Maxio:ProductFamilyHandle`, and optional `Maxio:BaseUrl` (used verbatim as the API base address when set).
Keep values in user-secrets or environment variables (`Maxio__ApiKey`, ...), never in the repository:

```
dotnet user-secrets set "Maxio:ApiKey" "<key>" --project src/PublicApi
```

The project references the Maxio .NET SDK from the maxio plugin directory (a machine-local `ProjectReference`); re-point it with `dotnet remove/add reference` on another machine.
