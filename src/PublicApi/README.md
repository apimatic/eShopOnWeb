# API Endpoints

This folder demonstrates how to configure API endpoints as individual classes. You can compare it to the traditional controller-based approach found in /Web/Controllers/Api.

## Endpoint groups

| Group | Routes |
|---|---|
| `AuthEndpoints` | `POST /api/authenticate` |
| `CatalogItemEndpoints` | `GET`, `POST`, `PUT`, `DELETE` on `/api/catalog-items` |
| `CatalogBrandEndpoints` | `GET /api/catalog-brands` |
| `CatalogTypeEndpoints` | `GET /api/catalog-types` |
| `SubscriptionEndpoints` | `GET /api/subscription-plans`, `POST /api/subscriptions`, `GET /api/my-subscriptions` |

## Subscription endpoints

Recurring-subscription billing backed by Maxio Advanced Billing. All three routes require a JWT, and
the shopper identity is taken from the token rather than from the request. See
[MAXIO-SUBSCRIPTIONS.md](../../MAXIO-SUBSCRIPTIONS.md) for the design, the configuration keys and the
idempotency guarantees.

`POST /api/subscriptions` accepts an optional `Idempotency-Key` header (or an `idempotencyKey` body
field). It answers **201** when it creates a subscription and **200** with `alreadySubscribed: true`
when the shopper already held one, so a repeated subscribe is reported rather than rejected.
