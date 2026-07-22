using System.Text.Json;

namespace Microsoft.eShopWeb.MaxioIntegrationTests;

/// <summary>
/// Provider payloads in Maxio's own wire format. Building them from the real snake_case field names means
/// the tests exercise the SDK's actual deserialization path — envelopes, unions and money magnitudes
/// included — rather than a convenient fiction.
/// </summary>
public static class MaxioPayloads
{
    public const int FAMILY_ID = 3026730;
    public const int PRO_ID = 7130997;
    public const int BASIC_ID = 7130998;
    public const int COMPONENT_ID = 3062733;
    public const int SUBSCRIPTION_ID = 42;
    public const int CUSTOMER_ID = 7;
    public const string CUSTOMER_REFERENCE = "demouser@microsoft.com";
    public const string PERIOD_START = "2026-07-01T00:00:00-04:00";
    public const string PERIOD_END = "2026-08-01T00:00:00-04:00";

    private static string Serialize(object value) => JsonSerializer.Serialize(value);

    public static string ProductFamilies(string handle = MaxioTestContext.FAMILY_HANDLE)
    {
        return Serialize(new[]
        {
            new Dictionary<string, object?>
            {
                ["product_family"] = new Dictionary<string, object?>
                {
                    ["id"] = FAMILY_ID,
                    ["name"] = "eShopSubscribe",
                    ["handle"] = handle
                }
            }
        });
    }

    public static string EmptyProductFamilies() => "[]";

    /// <summary>$299.00 per month, expressed the way Maxio sends it: an integer number of cents.</summary>
    public static object ProProductBody(string? archivedAt = null) => new Dictionary<string, object?>
    {
        ["product"] = new Dictionary<string, object?>
        {
            ["id"] = PRO_ID,
            ["name"] = "Pro Plan",
            ["handle"] = MaxioTestContext.PRO_HANDLE,
            ["description"] = "Everything in Basic, plus priority support.",
            ["price_in_cents"] = 29900,
            ["interval"] = 1,
            ["interval_unit"] = "month",
            ["require_credit_card"] = false,
            ["archived_at"] = archivedAt
        }
    };

    /// <summary>$29.00 per month.</summary>
    public static object BasicProductBody() => new Dictionary<string, object?>
    {
        ["product"] = new Dictionary<string, object?>
        {
            ["id"] = BASIC_ID,
            ["name"] = "Basic Plan",
            ["handle"] = MaxioTestContext.BASIC_HANDLE,
            ["description"] = "The essentials.",
            ["price_in_cents"] = 2900,
            ["interval"] = 1,
            ["interval_unit"] = "month",
            ["require_credit_card"] = false,
            ["archived_at"] = null
        }
    };

    public static string ProProduct(string? archivedAt = null) => Serialize(ProProductBody(archivedAt));

    public static string BasicProduct() => Serialize(BasicProductBody());

    public static string ProductList() => Serialize(new[] { ProProductBody(), BasicProductBody() });

    public static string EmptyProductList() => "[]";

    /// <summary>$0.01 per unit, sent as a decimal string in major units — not as cents.</summary>
    public static string MeteredComponent(string kind = "metered_component",
        string familyHandle = MaxioTestContext.FAMILY_HANDLE,
        string? unitPrice = "0.01",
        long? pricePerUnitInCents = null)
    {
        return Serialize(new Dictionary<string, object?>
        {
            ["component"] = new Dictionary<string, object?>
            {
                ["id"] = COMPONENT_ID,
                ["name"] = "API Calls",
                ["handle"] = MaxioTestContext.COMPONENT_HANDLE,
                ["kind"] = kind,
                ["unit_name"] = "call",
                ["pricing_scheme"] = "per_unit",
                ["unit_price"] = unitPrice,
                ["price_per_unit_in_cents"] = pricePerUnitInCents,
                ["product_family_id"] = FAMILY_ID,
                ["product_family_handle"] = familyHandle
            }
        });
    }

    public static string Customer(string reference = CUSTOMER_REFERENCE)
    {
        return Serialize(new Dictionary<string, object?>
        {
            ["customer"] = new Dictionary<string, object?>
            {
                ["id"] = CUSTOMER_ID,
                ["first_name"] = "Demo",
                ["last_name"] = "User",
                ["email"] = reference,
                ["reference"] = reference
            }
        });
    }

    public static object SubscriptionBody(string state = "active",
        string productHandle = MaxioTestContext.PRO_HANDLE,
        long productPriceInCents = 29900,
        bool cancelAtEndOfPeriod = false,
        string? delayedCancelAt = null,
        string? nextProductHandle = null,
        string customerReference = CUSTOMER_REFERENCE)
    {
        return new Dictionary<string, object?>
        {
            ["subscription"] = new Dictionary<string, object?>
            {
                ["id"] = SUBSCRIPTION_ID,
                ["state"] = state,
                ["current_period_started_at"] = PERIOD_START,
                ["current_period_ends_at"] = PERIOD_END,
                ["next_assessment_at"] = PERIOD_END,
                ["product_price_in_cents"] = productPriceInCents,
                ["cancel_at_end_of_period"] = cancelAtEndOfPeriod,
                ["delayed_cancel_at"] = delayedCancelAt,
                ["next_product_handle"] = nextProductHandle,
                ["customer"] = new Dictionary<string, object?>
                {
                    ["id"] = CUSTOMER_ID,
                    ["reference"] = customerReference,
                    ["email"] = customerReference
                },
                ["product"] = new Dictionary<string, object?>
                {
                    ["id"] = PRO_ID,
                    ["name"] = productHandle == MaxioTestContext.PRO_HANDLE ? "Pro Plan" : "Basic Plan",
                    ["handle"] = productHandle,
                    ["price_in_cents"] = productPriceInCents,
                    ["interval"] = 1,
                    ["interval_unit"] = "month"
                }
            }
        };
    }

    public static string Subscription(string state = "active",
        string productHandle = MaxioTestContext.PRO_HANDLE,
        long productPriceInCents = 29900,
        bool cancelAtEndOfPeriod = false,
        string? delayedCancelAt = null,
        string? nextProductHandle = null)
    {
        return Serialize(SubscriptionBody(state, productHandle, productPriceInCents, cancelAtEndOfPeriod, delayedCancelAt, nextProductHandle));
    }

    public static string SubscriptionList(params object[] subscriptions) => Serialize(subscriptions);

    public static string EmptySubscriptionList() => "[]";

    /// <summary>Usage quantity comes back as an int-or-string union; both forms occur in the wild.</summary>
    public static object UsageBody(long id = 9001, object? quantity = null) => new Dictionary<string, object?>
    {
        ["usage"] = new Dictionary<string, object?>
        {
            ["id"] = id,
            ["quantity"] = quantity ?? 5,
            ["memo"] = "eShopOnWeb order 1001",
            ["component_id"] = COMPONENT_ID,
            ["subscription_id"] = SUBSCRIPTION_ID,
            ["created_at"] = "2026-07-15T10:00:00-04:00"
        }
    };

    public static string Usage(long id = 9001, object? quantity = null) => Serialize(UsageBody(id, quantity));

    public static string UsageList(params object[] usages) => Serialize(usages);

    public static string EmptyUsageList() => "[]";

    public static string SubscriptionComponent(int unitBalance)
    {
        return Serialize(new Dictionary<string, object?>
        {
            ["component"] = new Dictionary<string, object?>
            {
                ["id"] = 1,
                ["component_id"] = COMPONENT_ID,
                ["subscription_id"] = SUBSCRIPTION_ID,
                ["kind"] = "metered_component",
                ["unit_name"] = "call",
                ["unit_balance"] = unitBalance
            }
        });
    }

    /// <summary>A downgrade preview: $150.00 credit, $14.50 new charge, $135.50 net credit.</summary>
    public static string MigrationPreview(long proratedAdjustmentInCents = -15000,
        long chargeInCents = 1450,
        long paymentDueInCents = -13550,
        long creditAppliedInCents = 0)
    {
        return Serialize(new Dictionary<string, object?>
        {
            ["migration"] = new Dictionary<string, object?>
            {
                ["prorated_adjustment_in_cents"] = proratedAdjustmentInCents,
                ["charge_in_cents"] = chargeInCents,
                ["payment_due_in_cents"] = paymentDueInCents,
                ["credit_applied_in_cents"] = creditAppliedInCents
            }
        });
    }

    public static string DelayedCancellation()
    {
        return Serialize(new Dictionary<string, object?>
        {
            ["message"] = "Subscription will be canceled at the end of the current period."
        });
    }

    /// <summary>The provider's 422 validation shape.</summary>
    public static string ErrorList(params string[] errors)
    {
        return Serialize(new Dictionary<string, object?> { ["errors"] = errors });
    }
}
