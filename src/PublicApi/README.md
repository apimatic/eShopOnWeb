# API Endpoints

This folder demonstrates how to configure API endpoints as individual classes. You can compare it to the traditional controller-based approach found in /Web/Controllers/Api.


## Subscription billing (Maxio)

Billing endpoints (`/api/plans`, `/api/subscriptions/...`, `/api/admin/plans/...`, `/api/admin/subscriptions/...`) call
Maxio Advanced Billing, the system of record, through its C# SDK. Settings come from the `Maxio:` configuration
section: `Maxio:ApiKey`, `Maxio:Subdomain`, `Maxio:Environment` (`US` or `EU`, blank means US) and the optional
`Maxio:BaseUrl`, which when set is used verbatim as the API base address instead of one derived from the subdomain.
The host refuses to start without an API key and a subdomain (or a base URL).

Never put these values in a file in the repository. For local development load them into user-secrets, e.g.
`dotnet user-secrets set "Maxio:ApiKey" "<value of MAXIO_API_KEY>" --project src/PublicApi`; elsewhere use the
`Maxio__ApiKey`, `Maxio__Subdomain`, ... environment variables. The integration tests in
`tests/PublicApiIntegrationTests/BillingEndpoints` run against the Maxio sandbox and need the same settings.

The project references the SDK shipped with the `maxio` plugin by its install path; re-point the reference
(`dotnet remove reference`, then `dotnet add reference <plugin>/sdk/dotnet/src/MaxioAdvancedBilling/MaxioAdvancedBilling.csproj`)
on a machine where the plugin lives elsewhere. See `maxio-advanced-billing-plan.md` at the repository root for the contract sheet and decisions.
