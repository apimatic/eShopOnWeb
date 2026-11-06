# Subscription billing for eShopOnWeb, with Maxio

Add subscription billing to eShopOnWeb, with Maxio as the billing system of record. Call Maxio through the SDK whose C# source is at `sdk/maxio`. Test the integration against the Maxio sandbox.

Add each feature below to `src/PublicApi` as an endpoint under `/api/`, following that project's conventions: the caller's identity and role come from the JWT, and the staff features are for the administrator role the project already uses. Each feature gives a suggested route. When the app can't carry out a request, the endpoint answers with a 4xx status and a reason. Do not stub Maxio or fabricate its responses.

## Features

1. **Plans.** A shopper lists the plans they can subscribe to. Suggested route: `GET /api/plans`.
2. **Subscribe.** A signed-in shopper subscribes to a plan without giving card details. A shopper can hold subscriptions to several plans at once. Suggested route: `POST /api/subscriptions` with the plan's handle.
3. **Subscription page.** A shopper sees one of their subscriptions. The answer includes `paid`: `true` when the shopper owes nothing on the subscription, `false` when they still owe money on it. Suggested route: `GET /api/subscriptions/{subscriptionId}`.
4. **Pause and resume.** A shopper can pause a subscription and resume it later. Suggested routes: `POST /api/subscriptions/{subscriptionId}/pause` and `POST /api/subscriptions/{subscriptionId}/resume`.
5. **Cancel and restart.** A shopper can cancel a subscription and restart one they cancelled. Suggested routes: `POST /api/subscriptions/{subscriptionId}/cancel` and `POST /api/subscriptions/{subscriptionId}/restart`.
6. **Switch plan.** A shopper can switch a subscription to another plan at any time, including a subscription they cancelled; switching puts them on the new plan. Suggested route: `POST /api/subscriptions/{subscriptionId}/switch` with the new plan's handle.
7. **Usage for a day.** Plans include metered usage, which another system records in Maxio. A shopper sees their metered usage on any single day they pick. Suggested route: `GET /api/subscriptions/{subscriptionId}/usage?date=YYYY-MM-DD`.
8. **New plans.** Staff add a plan with its name, monthly price and brand. Each plan belongs to one of the brands in the shop's catalog. Suggested route: `POST /api/admin/plans`.
9. **Price in another currency.** Staff can price a plan in another currency. Suggested route: `POST /api/admin/plans/{planId}/currency-prices` with the currency and the price.
10. **Next billing date.** Staff set the date a subscriber is next billed, and the subscriber is billed on that date. Suggested route: `PUT /api/admin/subscriptions/{subscriptionId}/next-billing-date` with the date.

## Settings

Read the Maxio settings from the `Maxio:` configuration section using exactly these keys, and hard-code none of their values: `Maxio:ApiKey`, `Maxio:Subdomain`, `Maxio:Environment` and `Maxio:BaseUrl`. The sandbox credentials are in the environment variables `MAXIO_API_KEY`, `MAXIO_SITE_SUBDOMAIN` and `MAXIO_ENVIRONMENT`. `Maxio:BaseUrl` is an optional override: when it is set, use it verbatim as the API's base address instead of deriving one from the subdomain.

---

## Constraints

- **Secrets never enter the repository.** Read the API credentials from the environment
  variables above and load them into **.NET user-secrets** yourself. Never write their
  **values** into any file inside this repository — not into `appsettings*.json`, not into
  a launch profile, a script, a test fixture, a comment, or a commit message. Referencing
  the variable/secret **names** is fine, the values are not.
- **You are running headless — there is no one to answer you.** Work until the integration
  is fully complete. Never hand back, never end with a question, and never defer remaining
  work to the user: decide and proceed.

