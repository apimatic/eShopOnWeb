# Pay by bank for eShopOnWeb, with Plaid

Add pay-by-bank to eShopOnWeb, with Plaid. Call Plaid through the SDK whose C# source is at `sdk/plaid`. Test the integration against the Plaid sandbox.

Add each feature below to `src/PublicApi` as an endpoint under `/api/`, following that project's conventions: the caller's identity and role come from the JWT, and the staff features are for the administrator role the project already uses. Each feature gives a suggested route. When the app can't carry out a request, the endpoint answers with a 4xx status and a reason. Do not stub Plaid or fabricate its responses.

## Features

1. **Bank picker.** Checkout has the shop's own bank picker, which lists the banks Plaid supports and searches them by name. Every checkout opens it at once, and a shopper can't pay by bank without it. Suggested route: `GET /api/banks`, with an optional `query`.
2. **Connect a bank.** A shopper connects a bank account through Plaid Link to pay by bank, or a brokerage account for the portfolio page. A shopper connects a bank on one visit and can pay with it on a later visit. Suggested routes: `POST /api/bank/link-token` for what the browser needs to open Link, and `POST /api/bank/connections` to save a connection from what Link hands the browser.
3. **Disconnect a bank.** A shopper can disconnect a bank or brokerage account in their account settings. After that, the shop stops paying Plaid for that account. Suggested route: `DELETE /api/bank/connections/{connectionId}`.
4. **Start a bank payment.** At checkout the shopper picks a connected bank to pay for the order. Its answer includes the order total and `options`: the payment speeds available for this order, from `standard` and `express`. Express moves the money the same business day, for a fee the shopper pays. Wholesale buyers place orders of any size and can pay for them by bank. Suggested route: `POST /api/checkout/bank-payment` with the connection, the items and the shipping cost.
5. **Change the shipping.** The shipping cost can change after the payment is started, for example when it is recalculated; confirming then charges the new total, without the shopper starting again. Suggested route: `PUT /api/checkout/bank-payment/{checkoutId}/shipping` with the new shipping cost.
6. **Confirm.** The shopper confirms with one of the offered speeds and is charged. The answer is the order confirmation, with the order id, the `fee` charged and `arrivesOn`, the date the money is expected to arrive. Suggested route: `POST /api/checkout/bank-payment/{checkoutId}/confirm` with the speed.
7. **Cancel an order.** A shopper can cancel an order paid by bank at any time before it ships, and gets all their money back. Suggested route: `POST /api/orders/{orderId}/cancel`.
8. **Bank activity for support.** Support staff can see the activity on a shopper's connected bank accounts, to check a disputed payment. The shop keeps its own copy of that activity, pending transactions included, and keeps it in step with the bank's records as they change. The view covers every bank the shopper has connected. It answers with `transactions`, each with its `name`, amount, date and `pending`. Suggested route: `GET /api/support/shoppers/{email}/bank-activity`.
9. **Transactions between two dates.** While on the phone with a shopper, support staff can look up the transactions on any of the shopper's connected banks between two dates, including banks connected before this lookup existed. The answer comes straight from the bank and lists `transactions`, each with its `name`, amount and date. Suggested route: `GET /api/support/shoppers/{email}/transactions?from=YYYY-MM-DD&to=YYYY-MM-DD`.
10. **Portfolio.** A shopper who connected a brokerage account sees their holdings. The answer lists each position with the security's ticker and name, its `value`, and how much of it the shopper holds: for an option, `contracts`, the number of contracts held; for anything else, `quantity`. Suggested route: `GET /api/portfolio`.

## Settings

Read the Plaid settings from the `Plaid:` configuration section using exactly these keys, and hard-code none of their values: `Plaid:ClientId`, `Plaid:Secret`, `Plaid:Environment` and `Plaid:BaseUrl`. The sandbox credentials are in the environment variables `PLAID_CLIENT_ID`, `PLAID_SECRET` and `PLAID_ENV`. `Plaid:BaseUrl` is an optional override: when it is set, use it verbatim as the API's base address instead of deriving one from the environment.

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

