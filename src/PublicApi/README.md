# API Endpoints

This folder demonstrates how to configure API endpoints as individual classes. You can compare it to the traditional controller-based approach found in /Web/Controllers/Api.


## Maxio subscription billing

PublicApi exposes recurring-subscription billing backed by Maxio Advanced Billing:
`GET /api/subscription-plans`, `POST /api/subscriptions` (body `{"planHandle":"..."}`), `GET /api/my-subscriptions` (all require a JWT from `POST /api/authenticate`).

Settings (section `Maxio:`): `ApiKey`, `Subdomain`, `ProductFamilyHandle`, optional `BaseUrl` (used verbatim when set), optional `Environment` (`US`/`EU`), optional `PaymentCollectionMethod` (default `remittance`).
Load them into user-secrets from the `MAXIO_API_KEY`, `MAXIO_SITE_SUBDOMAIN`, `MAXIO_DEFAULT_PRODUCT_FAMILY`, `MAXIO_ENVIRONMENT` environment variables:

    dotnet user-secrets set "Maxio:ApiKey" $env:MAXIO_API_KEY --project src/PublicApi
    dotnet user-secrets set "Maxio:Subdomain" $env:MAXIO_SITE_SUBDOMAIN --project src/PublicApi
    dotnet user-secrets set "Maxio:ProductFamilyHandle" $env:MAXIO_DEFAULT_PRODUCT_FAMILY --project src/PublicApi
    dotnet user-secrets set "Maxio:Environment" $env:MAXIO_ENVIRONMENT --project src/PublicApi
