---
name: feedback-maxio-remittance-payment-collection
description: Maxio subscription create fails with "No payment method was on file" unless payment_collection_method=remittance is sent for cardless signups
metadata:
  type: feedback
---

When creating a Maxio Advanced Billing subscription for a plan configured with "payment
method not required" and no card is supplied, `POST /subscriptions.json` still returns a 422
(`"No payment method was on file for the $X.XX balance"`) unless the request explicitly sets
`payment_collection_method: "remittance"`.

**Why**: the default `payment_collection_method` is `automatic`, which always tries to charge
a card at signup regardless of the product's "require credit card" setting. The spec's own
"Basic" example for `createSubscription` in `maxio-spec/openapi.yaml` shows exactly this
(`payment_collection_method: remittance` alongside a card-less `customer_attributes` block) —
it isn't a workaround, it's the documented pattern for this scenario.

**How to apply**: [[project-maxio-subscription-integration]] — any code that creates a Maxio
subscription without collecting card details must set `payment_collection_method` to
`remittance` (or otherwise handle billing so a card is present). `MaxioCreateSubscription` in
`src/Infrastructure/Maxio/Contracts/MaxioSubscription.cs` defaults this field to `"remittance"`
already — don't remove that default without re-verifying against a real sandbox subscribe call.
