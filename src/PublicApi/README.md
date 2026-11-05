# API Endpoints

This folder demonstrates how to configure API endpoints as individual classes. You can compare it to the traditional controller-based approach found in /Web/Controllers/Api.


## Subscriptions (Maxio Advanced Billing)

JWT-authenticated endpoints (get a token from `POST /api/authenticate`):

- `GET /api/subscription-plans` - plans in the configured product family
- `POST /api/subscriptions` with `{"planHandle":"<handle>"}` - ensures a Maxio customer (keyed on the user id) and subscribes; idempotent per user and plan (201 created, 200 already subscribed)
- `GET /api/my-subscriptions` - the caller's subscriptions (plan, price, state, next billing date)

Configuration (`Maxio:` section): `ApiKey`, `Subdomain`, `ProductFamilyHandle`, optional `BaseUrl` (used verbatim when set), optional `Environment` (US/EU). Keep the API key out of source control:

```
dotnet user-secrets set "Maxio:ApiKey" "<key>" --project src/PublicApi
```
