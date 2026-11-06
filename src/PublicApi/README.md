# API Endpoints

This folder demonstrates how to configure API endpoints as individual classes. You can compare it to the traditional controller-based approach found in /Web/Controllers/Api.


## Subscription billing (Maxio)

Billing endpoints (`/api/plans`, `/api/subscriptions/...`, `/api/admin/plans/...`, `/api/admin/subscriptions/...`) call the Maxio API over HTTP. Configure the `Maxio:` section: `Maxio:ApiKey`, `Maxio:Subdomain`, `Maxio:Environment` (`US` or `EU`) and the optional `Maxio:BaseUrl` override (used verbatim). Load them with `dotnet user-secrets set "Maxio:ApiKey" <value>`; never commit values.

A catalog brand maps to the Maxio product family with handle `catalog-brand-{brandId}`; shoppers map to Maxio customers by reference (their user name).
