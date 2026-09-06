# API Endpoints

This folder demonstrates how to configure API endpoints as individual classes. You can compare it to the traditional controller-based approach found in /Web/Controllers/Api.

## Subscription billing

`SubscriptionEndpoints/` adds recurring-subscription billing backed by Maxio Advanced Billing,
running in parallel to the one-time Catalog/Basket/Order flow. See
[SubscriptionEndpoints/README.md](SubscriptionEndpoints/README.md) for the endpoints, the
configuration keys and how enrollment stays idempotent.
