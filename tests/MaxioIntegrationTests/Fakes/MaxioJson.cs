namespace Microsoft.eShopWeb.MaxioIntegrationTests.Fakes;

/// <summary>
/// Maxio wire payloads, written in the provider's own JSON rather than built from SDK models.
/// </summary>
/// <remarks>
/// Hand-written JSON is deliberate: it means these tests would catch a wire-name or unit change,
/// which a fixture round-tripped through the SDK's own serializer could never do.
/// </remarks>
internal static class MaxioJson
{
    internal const int ProductFamilyId = 3026731;
    internal const int ProPlanId = 7130999;
    internal const int BasicPlanId = 7131000;
    internal const int ComponentId = 3062734;
    internal const int CustomerId = 90210;
    internal const int SubscriptionId = 55555;

    internal const string UserReference = "demouser@microsoft.com";

    internal static string ProductFamilyList(string handle = "eshop-subscribe") => $$"""
        [
          { "product_family": { "id": {{ProductFamilyId}}, "name": "eShopSubscribe", "handle": "{{handle}}" } },
          { "product_family": { "id": 999, "name": "Unrelated", "handle": "something-else" } }
        ]
        """;

    /// <summary>$299.00/month, expressed the way Maxio does — in integer cents.</summary>
    internal static string ProPlan() => $$"""
        {
          "id": {{ProPlanId}},
          "name": "Pro Plan",
          "handle": "eshop-pro",
          "description": "Priority support and higher limits.",
          "price_in_cents": 29900,
          "interval": 1,
          "interval_unit": "month",
          "require_credit_card": false,
          "request_credit_card": true,
          "product_family": { "id": {{ProductFamilyId}}, "handle": "eshop-subscribe" }
        }
        """;

    /// <summary>$29.00/month.</summary>
    internal static string BasicPlan() => $$"""
        {
          "id": {{BasicPlanId}},
          "name": "Basic Plan",
          "handle": "basic-plan",
          "price_in_cents": 2900,
          "interval": 1,
          "interval_unit": "month",
          "require_credit_card": false,
          "product_family": { "id": {{ProductFamilyId}}, "handle": "eshop-subscribe" }
        }
        """;

    internal static string ProductList(params string[] products) =>
        "[" + string.Join(",", products.Select(p => $"{{ \"product\": {p} }}")) + "]";

    /// <summary>A metered component priced at one cent per unit, in dollars as Maxio reports it.</summary>
    internal static string MeteredComponent(string kind = "metered_component",
        string unitPrice = "0.01",
        string familyHandle = "eshop-subscribe") => $$"""
        {
          "component": {
            "id": {{ComponentId}},
            "name": "API Calls",
            "handle": "api-call",
            "kind": "{{kind}}",
            "pricing_scheme": "per_unit",
            "unit_name": "api_call",
            "unit_price": "{{unitPrice}}",
            "product_family_id": {{ProductFamilyId}},
            "product_family_handle": "{{familyHandle}}"
          }
        }
        """;

    /// <summary>A component that reports only the cents field, exercising the fallback conversion.</summary>
    internal static string MeteredComponentPricedInCentsOnly(long pricePerUnitInCents) => $$"""
        {
          "component": {
            "id": {{ComponentId}},
            "name": "API Calls",
            "handle": "api-call",
            "kind": "metered_component",
            "pricing_scheme": "per_unit",
            "price_per_unit_in_cents": {{pricePerUnitInCents}},
            "product_family_id": {{ProductFamilyId}},
            "product_family_handle": "eshop-subscribe"
          }
        }
        """;

    internal static string Customer(string reference = UserReference, int id = CustomerId) => $$"""
        {
          "customer": {
            "id": {{id}},
            "reference": "{{reference}}",
            "email": "{{reference}}",
            "first_name": "Demouser",
            "last_name": "eShopOnWeb"
          }
        }
        """;

    /// <summary>A customer record with no reference, as one created outside eShopOnWeb would look.</summary>
    internal static string CustomerWithoutReference(int id = CustomerId) => $$"""
        {
          "customer": {
            "id": {{id}},
            "email": "someone-else@example.com",
            "first_name": "Someone",
            "last_name": "Else"
          }
        }
        """;

    internal static string Subscription(string state = "active",
        string? product = null,
        long balanceInCents = 0,
        bool cancelAtEndOfPeriod = false,
        string? delayedCancelAt = null,
        string? nextProductHandle = null,
        int id = SubscriptionId,
        string reference = UserReference) => $$"""
        {
          "subscription": {
            "id": {{id}},
            "state": "{{state}}",
            "balance_in_cents": {{balanceInCents}},
            "current_period_started_at": "2026-07-01T00:00:00-04:00",
            "current_period_ends_at": "2026-08-01T00:00:00-04:00",
            "next_assessment_at": "2026-08-01T00:00:00-04:00",
            "activated_at": "2026-07-01T00:00:00-04:00",
            "cancel_at_end_of_period": {{(cancelAtEndOfPeriod ? "true" : "false")}},
            {{(delayedCancelAt is null ? "" : $"\"delayed_cancel_at\": \"{delayedCancelAt}\",")}}
            {{(nextProductHandle is null ? "" : $"\"next_product_handle\": \"{nextProductHandle}\",")}}
            "product": {{product ?? ProPlan()}},
            "customer": {
              "id": {{CustomerId}},
              "reference": "{{reference}}",
              "email": "{{reference}}"
            }
          }
        }
        """;

    internal static string SubscriptionList(params string[] subscriptionEnvelopes) =>
        "[" + string.Join(",", subscriptionEnvelopes) + "]";

    internal static string Usage(long id = 777, string quantity = "3", string memo = "eShopOnWeb order 1") => $$"""
        {
          "usage": {
            "id": {{id}},
            "quantity": {{quantity}},
            "memo": "{{memo}}",
            "created_at": "2026-07-15T10:00:00-04:00",
            "component_id": {{ComponentId}},
            "subscription_id": {{SubscriptionId}}
          }
        }
        """;

    internal static string UsageList(params string[] usageEnvelopes) =>
        "[" + string.Join(",", usageEnvelopes) + "]";

    /// <summary>
    /// A migration preview. Every amount is in integer cents, and Maxio signs a credit negatively.
    /// </summary>
    internal static string MigrationPreview(long chargeInCents,
        long creditAppliedInCents,
        long? paymentDueInCents = null,
        long proratedAdjustmentInCents = 0) => $$"""
        {
          "migration": {
            "prorated_adjustment_in_cents": {{proratedAdjustmentInCents}},
            "charge_in_cents": {{chargeInCents}},
            "payment_due_in_cents": {{paymentDueInCents ?? Math.Max(0, chargeInCents - Math.Abs(creditAppliedInCents))}},
            "credit_applied_in_cents": {{creditAppliedInCents}}
          }
        }
        """;

    internal static string DelayedCancellation() =>
        """{ "message": "Successfully initiated delayed cancellation" }""";

    /// <summary>Maxio's 422 shape: a list of human-readable messages.</summary>
    internal static string ErrorList(params string[] errors) =>
        $"{{ \"errors\": [{string.Join(",", errors.Select(e => $"\"{e}\""))}] }}";

    internal const string EmptyList = "[]";
}
