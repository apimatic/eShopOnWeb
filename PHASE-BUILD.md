# Card and bank payments for eShopOnWeb, with Stax

Add Stax payments to eShopOnWeb. Call Stax through the SDK whose C# source is at `sdk/stax`. Test the integration against the Stax sandbox.

Add each feature below to `src/PublicApi` as an endpoint under `/api/`, following that project's conventions: the caller's identity and role come from the JWT, and the staff features are for the administrator role the project already uses. Each feature gives a suggested route. When the app can't carry out a request, the endpoint answers with a 4xx status and a reason. Do not stub Stax or fabricate its responses.

## Features

1. **Saved payment methods.** A shopper can save a card or a bank account, see their saved ones and remove any of them. Suggested routes: `POST /api/payment-methods`, `GET /api/payment-methods` and `DELETE /api/payment-methods/{paymentMethodId}`.
2. **Orders.** A shopper places an order for items from the catalog. Suggested route: `POST /api/orders`.
3. **Checkout.** Before paying, checkout shows the shopper the exact amount they will pay with the payment method they picked. The shop passes its card processing fees on to shoppers. The answer includes the `fee` and the `total`. Suggested route: `POST /api/orders/{orderId}/checkout` with the payment method.
4. **Pay.** A shopper pays an order with a saved card or bank account. Suggested route: `POST /api/orders/{orderId}/pay` with the payment method.
5. **Order status.** A shopper sees their order. The answer includes `paid`: whether the order's payment has gone through. A bank payment goes through a few days after the shopper pays, when the bank settles it, and its order then shows `paid` without anyone checking by hand. Suggested route: `GET /api/orders/{orderId}`.
6. **Payment methods for support.** Support staff can see every card and bank account a shopper has ever saved, including removed ones with when each was removed (`removedAt`), and including ones removed before this screen existed. Suggested route: `GET /api/admin/shoppers/{email}/payment-methods`.
7. **Close an account.** A shopper can close their shop account. Closing it erases the shopper's data, at Stax as well as in the shop. Suggested route: `POST /api/account/close`.
8. **Refunds.** Staff can refund an order from the admin area, whether it was paid by card or by bank account. Once staff refund an order, the shopper gets the whole amount back without staff doing anything more. The refund's answer and the staff view of the order include `refundStatus`, which says where the refund stands. Suggested routes: `POST /api/admin/orders/{orderId}/refunds` and `GET /api/admin/orders/{orderId}`.
9. **Monthly invoices.** Staff can set up a monthly invoice for a business customer, with the customer's email and company, the amount and the day of the first invoice: today, unless staff pick a later day. Staff can pause a schedule and resume it later. Suggested routes: `POST /api/admin/invoice-schedules`, `POST /api/admin/invoice-schedules/{scheduleId}/pause` and `POST /api/admin/invoice-schedules/{scheduleId}/resume`.
10. **Sellers.** The shop is also becoming a marketplace: an administrator can onboard a seller as a new merchant from the admin area. Suggested route: `POST /api/admin/sellers` with the seller's company and contact.

## Settings

Read the Stax settings from the `Stax:` configuration section using exactly these keys, and hard-code none of their values: `Stax:ApiKey`, `Stax:PlatformApiKey` and `Stax:BaseUrl`. The sandbox credentials are in `STAX_API_KEY`; the platform key, when the shop has one, is in `STAX_PLATFORM_API_KEY`. `Stax:BaseUrl` is an optional override: when it is set, use it verbatim as the API's base address instead of the public one.

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

