# API Endpoints

This folder demonstrates how to configure API endpoints as individual classes. You can compare it to the traditional controller-based approach found in /Web/Controllers/Api.


## Subscription billing (Maxio)

Billing endpoints call the Maxio HTTP API directly. Configure the `Maxio:` section
(`Maxio:ApiKey`, `Maxio:Subdomain`, `Maxio:Environment`, optional `Maxio:BaseUrl`) with
`dotnet user-secrets set "Maxio:ApiKey" "<value>"` — never in a tracked file. The integration tests in
`tests/PublicApiIntegrationTests/BillingEndpoints` run against the Maxio sandbox.
