# API Endpoints

This folder demonstrates how to configure API endpoints as individual classes. You can compare it to the traditional controller-based approach found in /Web/Controllers/Api.

## Subscriptions

The JWT-protected subscription endpoints are `/api/subscription-plans`,
`/api/subscriptions`, and `/api/my-subscriptions`. Maxio Advanced Billing is the
system of record. Configure the `Maxio` section only with `ApiKey`, `Subdomain`,
`ProductFamilyHandle`, and optional `BaseUrl`. In local development, load the
values from `MAXIO_API_KEY`, `MAXIO_SITE_SUBDOMAIN`, and
`MAXIO_DEFAULT_PRODUCT_FAMILY` into this project's user-secrets; no Maxio secret
belongs in an appsettings file.
