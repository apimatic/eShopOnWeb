# API Endpoints

This folder demonstrates how to configure API endpoints as individual classes. You can compare it to the traditional controller-based approach found in /Web/Controllers/Api.


## Subscription billing (Maxio)

Subscriptions are billed through Maxio (the system of record) using the SDK in `sdk/maxio`.
Settings come from the `Maxio:` configuration section: `Maxio:ApiKey`, `Maxio:Subdomain`,
`Maxio:Environment` (`US`/`EU`) and the optional `Maxio:BaseUrl` (used verbatim as the API base
address when set). Keep them in user-secrets, never in the repo:

```
dotnet user-secrets set "Maxio:ApiKey" "<value of MAXIO_API_KEY>" --project src/PublicApi
dotnet user-secrets set "Maxio:Subdomain" "<value of MAXIO_SITE_SUBDOMAIN>" --project src/PublicApi
dotnet user-secrets set "Maxio:Environment" "<value of MAXIO_ENVIRONMENT>" --project src/PublicApi
```

Notes: a shopper is a Maxio customer whose reference is the JWT user name; subscriptions are invoiced
(no card needed); a plan's brand is kept in the Maxio product's accounting code; plans live in the
site's existing product family so they share its metered components. Failures answer 4xx with
`{ "StatusCode", "Message" }`. Currency prices need the currency enabled on the Maxio site first.
