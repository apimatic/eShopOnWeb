# API Endpoints

This folder demonstrates how to configure API endpoints as individual classes. You can compare it to the traditional controller-based approach found in /Web/Controllers/Api.

## Subscription billing

`SubscriptionEndpoints/` adds recurring-subscription billing backed by Maxio Advanced Billing,
alongside the existing one-time checkout flow. See
[SubscriptionEndpoints/README.md](SubscriptionEndpoints/README.md) for the endpoints, the
configuration keys and how the integration maps onto the Maxio OpenAPI specification in
`maxio-spec/`.
