# API Endpoints

This folder demonstrates how to configure API endpoints as individual classes. You can compare it to the traditional controller-based approach found in /Web/Controllers/Api.


## Subscriptions (Maxio Advanced Billing)

`GET /api/subscription-plans`, `POST /api/subscriptions` (`{"planHandle": "..."}`) and `GET /api/my-subscriptions` (JWT bearer).
Configuration comes from the `Maxio:` section: `Maxio:ApiKey`, `Maxio:Subdomain`, `Maxio:ProductFamilyHandle` and the optional
`Maxio:BaseUrl` (used verbatim when set). Load them with `dotnet user-secrets set "Maxio:ApiKey" ... --project src/PublicApi`;
the host refuses to start when a required one is missing. The SDK is referenced from the `maxio-advanced-billing` plugin source
(see the `ProjectReference` in `PublicApi.csproj`; re-point it where the plugin is installed elsewhere).
