# API Endpoints

This folder demonstrates how to configure API endpoints as individual classes. You can compare it to the traditional controller-based approach found in /Web/Controllers/Api.


## Subscription billing (Maxio)

Subscription endpoints live under `/api/plans`, `/api/subscriptions` and `/api/admin/*` and call the Maxio API over HTTP.
Settings come from the `Maxio:` section: `Maxio:ApiKey`, `Maxio:Subdomain`, `Maxio:Environment` (`US`/`EU`) and the optional
`Maxio:BaseUrl` (used verbatim when set). Keep them in user-secrets, e.g. `dotnet user-secrets set "Maxio:ApiKey" "<value>"`.
Shoppers are mapped to Maxio customers by username (customer reference); pricing a plan in another currency needs that currency enabled on the Maxio site.
