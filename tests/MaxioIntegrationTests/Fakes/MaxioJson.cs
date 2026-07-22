using System.Text.Json;

namespace Microsoft.eShopWeb.MaxioIntegrationTests.Fakes;

/// <summary>
/// Canned Maxio response bodies in the provider's own wire shape: snake_case field names,
/// single-field envelopes, and integer-cent money. Building them by serialising objects (rather
/// than pasting JSON literals) keeps the fixtures readable while still producing exactly the
/// snake_case wire format the SDK deserialises, so the tests assert against what the provider
/// really sends rather than against the mapping under test.
/// </summary>
public static class MaxioJson
{
    /// <summary>Pro Plan: $299.00 / month, which the provider expresses as 29900 cents.</summary>
    public const int ProPlanPriceInCents = 29_900;

    /// <summary>Basic Plan: $29.00 / month, which the provider expresses as 2900 cents.</summary>
    public const int BasicPlanPriceInCents = 2_900;

    public const int ProPlanId = 7126957;
    public const int BasicPlanId = 7126958;

    private static readonly JsonSerializerOptions WireFormat = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower
    };

    private static string Serialise(object value) => JsonSerializer.Serialize(value, WireFormat);

    public static string ProductFamilies(params (int Id, string Handle)[] families) =>
        Serialise(families.Select(family => new
        {
            ProductFamily = new { family.Id, Name = "eShopSubscribe", family.Handle }
        }));

    private static object Product(int id, string handle, string name, int priceInCents,
        string intervalUnit = "month", string? archivedAt = null) => new
        {
            Id = id,
            Name = name,
            Handle = handle,
            PriceInCents = priceInCents,
            Interval = 1,
            IntervalUnit = intervalUnit,
            Description = $"{name} description",
            ArchivedAt = archivedAt
        };

    public static string ProductResponse(int id, string handle, string name, int priceInCents) =>
        Serialise(new { Product = Product(id, handle, name, priceInCents) });

    /// <summary>Both demo plans, most expensive first, so ordering by price is actually exercised.</summary>
    public static string ProductList() =>
        Serialise(new[]
        {
            new { Product = Product(ProPlanId, "eshop-pro", "Pro Plan", ProPlanPriceInCents) },
            new { Product = Product(BasicPlanId, "basic-plan", "Basic Plan", BasicPlanPriceInCents) }
        });

    /// <summary>A product list containing exactly the named plans.</summary>
    public static string ProductListOf(params (int Id, string Handle, string Name, int PriceInCents)[] plans) =>
        Serialise(plans.Select(plan => new
        {
            Product = Product(plan.Id, plan.Handle, plan.Name, plan.PriceInCents)
        }));

    /// <summary>A product list including an archived plan, which must never be offered to customers.</summary>
    public static string ProductListWithArchived() =>
        Serialise(new[]
        {
            new { Product = Product(ProPlanId, "eshop-pro", "Pro Plan", ProPlanPriceInCents) },
            new { Product = Product(999, "retired", "Retired Plan", 100, "month", "2024-01-01T00:00:00Z") }
        });

    public static string Customer(int id, string reference, string email,
        string firstName = "Demo", string lastName = "User") =>
        Serialise(new
        {
            Customer = new
            {
                Id = id,
                Reference = reference,
                Email = email,
                FirstName = firstName,
                LastName = lastName,
                CreatedAt = "2026-01-05T09:00:00Z"
            }
        });

    /// <summary>
    /// A subscription in the provider's shape. <paramref name="state"/> is the provider's own wire
    /// value, for example <c>active</c>, <c>on_hold</c> or <c>canceled</c>.
    /// </summary>
    public static object Subscription(int id,
        string state,
        int customerId,
        string customerReference,
        string planHandle = "eshop-pro",
        int planId = ProPlanId,
        string planName = "Pro Plan",
        int productPriceInCents = ProPlanPriceInCents,
        bool cancelAtEndOfPeriod = false,
        string? scheduledCancellationAt = null,
        string? nextProductHandle = null,
        int? nextProductId = null,
        string? onHoldAt = null) => new
        {
            Id = id,
            State = state,
            CurrentPeriodStartedAt = "2026-07-01T00:00:00Z",
            CurrentPeriodEndsAt = "2026-08-01T00:00:00Z",
            NextAssessmentAt = "2026-08-01T00:00:00Z",
            ActivatedAt = "2026-07-01T00:00:00Z",
            CanceledAt = (string?)null,
            CancelAtEndOfPeriod = cancelAtEndOfPeriod,
            ScheduledCancellationAt = scheduledCancellationAt,
            OnHoldAt = onHoldAt,
            AutomaticallyResumeAt = (string?)null,
            ProductPriceInCents = productPriceInCents,
            Currency = "USD",
            NextProductId = nextProductId,
            NextProductHandle = nextProductHandle,
            Product = Product(planId, planHandle, planName, productPriceInCents),
            Customer = new
            {
                Id = customerId,
                Reference = customerReference,
                Email = customerReference,
                FirstName = "Demo",
                LastName = "User"
            }
        };

    public static string SubscriptionResponse(int id,
        string state,
        int customerId,
        string customerReference,
        string planHandle = "eshop-pro",
        int planId = ProPlanId,
        string planName = "Pro Plan",
        int productPriceInCents = ProPlanPriceInCents,
        bool cancelAtEndOfPeriod = false,
        string? scheduledCancellationAt = null,
        string? nextProductHandle = null,
        int? nextProductId = null,
        string? onHoldAt = null) =>
        Serialise(new
        {
            Subscription = Subscription(id, state, customerId, customerReference, planHandle, planId, planName,
                productPriceInCents, cancelAtEndOfPeriod, scheduledCancellationAt, nextProductHandle,
                nextProductId, onHoldAt)
        });

    public static string SubscriptionList(params object[] subscriptions) =>
        Serialise(subscriptions.Select(subscription => new { Subscription = subscription }));

    /// <summary>A component. <paramref name="kind"/> carries the provider's wire value for its kind.</summary>
    public static string ComponentResponse(int id,
        string handle,
        string kind = "metered_component",
        string unitPrice = "0.01") =>
        Serialise(new
        {
            Component = new
            {
                Id = id,
                Name = "API Calls",
                Handle = handle,
                Kind = kind,
                UnitName = "api call",
                UnitPrice = unitPrice,
                PricingScheme = "per_unit",
                PricePerUnitInCents = 1,
                Archived = false
            }
        });

    public static string UsageResponse(long id, int subscriptionId, int quantity,
        string memo = "", int componentId = 3057195) =>
        Serialise(new
        {
            Usage = new
            {
                Id = id,
                Quantity = quantity,
                Memo = memo,
                CreatedAt = "2026-07-20T12:00:00Z",
                ComponentId = componentId,
                ComponentHandle = "api-call",
                SubscriptionId = subscriptionId
            }
        });

    /// <summary>
    /// A usage list. The provider may send a quantity as a JSON number or as a string, so both
    /// forms are expressible here — pass an <see cref="int"/> or a <see cref="string"/>.
    /// </summary>
    public static string UsageList(params object[] quantities) =>
        Serialise(quantities.Select((quantity, index) => new
        {
            Usage = new
            {
                Id = index + 1,
                Quantity = quantity,
                Memo = string.Empty,
                CreatedAt = "2026-07-20T12:00:00Z",
                ComponentId = 3057195,
                ComponentHandle = "api-call",
                SubscriptionId = 42
            }
        }));

    /// <summary>A proration preview. All four amounts are integer cents on the wire.</summary>
    public static string MigrationPreview(int proratedAdjustmentInCents,
        int chargeInCents,
        int paymentDueInCents,
        int creditAppliedInCents) =>
        Serialise(new
        {
            Migration = new
            {
                ProratedAdjustmentInCents = proratedAdjustmentInCents,
                ChargeInCents = chargeInCents,
                PaymentDueInCents = paymentDueInCents,
                CreditAppliedInCents = creditAppliedInCents
            }
        });

    public static string DelayedCancellation(
        string message = "Subscription will be cancelled at the end of the period.") =>
        Serialise(new { Message = message });

    /// <summary>The provider's 422 shape for operations that return a list of error strings.</summary>
    public static string ErrorList(params string[] errors) => Serialise(new { Errors = errors });

    /// <summary>
    /// The 422 shape the customer operations are generated against, where <c>errors</c> is an
    /// object of per-field string arrays rather than a flat list.
    /// </summary>
    public static string CustomerErrors(params string[] errors) =>
        Serialise(new { Errors = new { PerPage = errors, PricePoint = Array.Empty<string>() } });

    public static string NotFound() => Serialise(new { Error = "Not Found" });
}
