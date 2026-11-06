# API Endpoints

This folder demonstrates how to configure API endpoints as individual classes. You can compare it to the traditional controller-based approach found in /Web/Controllers/Api.


## Maxio billing

Subscription billing calls the Maxio API over HTTP. Settings come from the `Maxio:` section
(`Maxio:ApiKey`, `Maxio:Subdomain`, `Maxio:Environment` = `US`/`EU`, optional `Maxio:BaseUrl` used verbatim).
Keep them in user-secrets, e.g. `dotnet user-secrets set "Maxio:ApiKey" "<key>"` from `src/PublicApi`.
