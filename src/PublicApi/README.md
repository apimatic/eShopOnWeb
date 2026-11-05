# API Endpoints

This folder demonstrates how to configure API endpoints as individual classes. You can compare it to the traditional controller-based approach found in /Web/Controllers/Api.


## Subscriptions (Maxio Advanced Billing)

JWT-authenticated endpoints backed by Maxio (system of record): `GET /api/subscription-plans`, `POST /api/subscriptions` (`{"planHandle":"..."}`), `GET /api/my-subscriptions`.

Configuration section `Maxio` (set with `dotnet user-secrets` or `Maxio__*` environment variables; never commit values): `ApiKey`, `Subdomain`, `ProductFamilyHandle`, optional `BaseUrl` (used verbatim as the API base address when set) and `Environment` (`US` default / `EU`).
The Maxio customer `reference` is the eShopOnWeb user name, so the user-to-subscription mapping lives in Maxio and survives restarts of the in-memory database.
