using System.Text.Json;

namespace Microsoft.eShopWeb.MaxioIntegrationTests.Fakes;

/// <summary>
/// Provider response bodies in Maxio's real wire shape — snake_case fields, money in cents, and every
/// payload wrapped one level down in its envelope. Built as objects and serialized with the provider's
/// naming convention, so the tests exercise the deserialization the integration really performs.
/// </summary>
internal static class MaxioPayloads
{
    public const string DefaultFamilyHandle = "eshop-subscribe";

    private static readonly JsonSerializerOptions WireFormat = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower
    };

    public static string Json(object value) => JsonSerializer.Serialize(value, WireFormat);

    public static string ProductFamilies(params (int Id, string Handle)[] families) =>
        Json(families.Select(family => new
        {
            ProductFamily = new
            {
                family.Id,
                Name = "eShopSubscribe",
                family.Handle,
                Description = "Subscription plans",
                CreatedAt = "2026-01-05T10:00:00-05:00"
            }
        }));

    public static object Product(
        int id = 7130993,
        string handle = "eshop-pro",
        string name = "Pro Plan",
        long priceInCents = 29_900,
        int interval = 1,
        string intervalUnit = "month",
        bool requireCreditCard = false,
        string familyHandle = DefaultFamilyHandle,
        string? archivedAt = null) => new
        {
            Id = id,
            Name = name,
            Handle = handle,
            Description = $"{name} description",
            PriceInCents = priceInCents,
            Interval = interval,
            IntervalUnit = intervalUnit,
            InitialChargeInCents = (long?)null,
            TrialPriceInCents = (long?)null,
            TrialInterval = (int?)null,
            ExpirationInterval = (int?)null,
            ExpirationIntervalUnit = "never",
            Taxable = false,
            RequireCreditCard = requireCreditCard,
            RequestCreditCard = false,
            ArchivedAt = archivedAt,
            ProductFamily = new { Id = 3026728, Name = "eShopSubscribe", Handle = familyHandle }
        };

    public static string ProductResponse(object product) => Json(new { Product = product });

    public static string ProductList(params object[] products) =>
        Json(products.Select(product => new { Product = product }));

    public static string ComponentResponse(
        int id = 3062731,
        string handle = "api-call",
        string name = "API Calls",
        string kind = "metered_component",
        string pricingScheme = "per_unit",
        string? unitPrice = "0.01",
        long? pricePerUnitInCents = null) =>
        Json(new
        {
            Component = new
            {
                Id = id,
                Name = name,
                Handle = handle,
                Kind = kind,
                PricingScheme = pricingScheme,
                UnitName = "call",
                Recurring = false,
                Archived = false,
                UnitPrice = unitPrice,
                PricePerUnitInCents = pricePerUnitInCents,
                ProductFamilyId = 3026728,
                ProductFamilyHandle = DefaultFamilyHandle
            }
        });

    public static object Customer(
        int id = 500123,
        string reference = "demouser@microsoft.com",
        string email = "demouser@microsoft.com",
        string firstName = "demouser",
        string lastName = "eShopOnWeb Customer") => new
        {
            Id = id,
            FirstName = firstName,
            LastName = lastName,
            Email = email,
            Reference = reference,
            CreatedAt = "2026-02-01T09:00:00-05:00",
            Verified = false
        };

    public static string CustomerResponse(object customer) => Json(new { Customer = customer });

    public static object Subscription(
        int id = 60001,
        string state = "active",
        string planHandle = "eshop-pro",
        string planName = "Pro Plan",
        long planPriceInCents = 29_900,
        long balanceInCents = 0,
        string customerReference = "demouser@microsoft.com",
        int customerId = 500123,
        bool cancelAtEndOfPeriod = false,
        string? onHoldAt = null,
        string? canceledAt = null,
        string? nextProductHandle = null,
        string periodStart = "2026-07-01T00:00:00-04:00",
        string periodEnd = "2026-08-01T00:00:00-04:00") => new
        {
            Id = id,
            State = state,
            BalanceInCents = balanceInCents,
            CurrentPeriodStartedAt = periodStart,
            CurrentPeriodEndsAt = periodEnd,
            NextAssessmentAt = periodEnd,
            ProductPriceInCents = planPriceInCents,
            TotalRevenueInCents = planPriceInCents,
            CancelAtEndOfPeriod = cancelAtEndOfPeriod,
            CanceledAt = canceledAt,
            OnHoldAt = onHoldAt,
            NextProductHandle = nextProductHandle,
            Product = Product(handle: planHandle, name: planName, priceInCents: planPriceInCents),
            Customer = Customer(id: customerId, reference: customerReference, email: customerReference)
        };

    public static string SubscriptionResponse(object subscription) => Json(new { Subscription = subscription });

    public static string SubscriptionList(params object[] subscriptions) =>
        Json(subscriptions.Select(subscription => new { Subscription = subscription }));

    public static string UsageResponse(
        long id = 900001,
        int quantity = 5,
        string memo = "eShopOnWeb order 42",
        int subscriptionId = 60001,
        string componentHandle = "api-call") =>
        Json(new
        {
            Usage = new
            {
                Id = id,
                Memo = memo,
                CreatedAt = "2026-07-20T12:00:00-04:00",
                Quantity = quantity,
                ComponentId = 3062731,
                ComponentHandle = componentHandle,
                SubscriptionId = subscriptionId
            }
        });

    public static string UsageList(params (long Id, int Quantity)[] usages) =>
        Json(usages.Select(usage => new
        {
            Usage = new
            {
                usage.Id,
                Memo = "usage",
                CreatedAt = "2026-07-20T12:00:00-04:00",
                usage.Quantity,
                ComponentId = 3062731,
                ComponentHandle = "api-call",
                SubscriptionId = 60001
            }
        }));

    public static string SubscriptionComponentResponse(int? unitBalance = 12) =>
        Json(new
        {
            Component = new
            {
                Id = 880001,
                ComponentId = 3062731,
                ComponentHandle = "api-call",
                Name = "API Calls",
                Kind = "metered_component",
                UnitName = "call",
                Enabled = true,
                UnitBalance = unitBalance,
                PricingScheme = "per_unit",
                SubscriptionId = 60001,
                Currency = "USD"
            }
        });

    public static string MigrationPreviewResponse(
        long chargeInCents = 27_000,
        long creditAppliedInCents = 2_600,
        long paymentDueInCents = 24_400,
        long proratedAdjustmentInCents = 24_400) =>
        Json(new
        {
            Migration = new
            {
                ChargeInCents = chargeInCents,
                PaymentDueInCents = paymentDueInCents,
                CreditAppliedInCents = creditAppliedInCents,
                ProratedAdjustmentInCents = proratedAdjustmentInCents
            }
        });

    public static string EmptyMigrationPreviewResponse() => Json(new { Migration = new { } });

    public static string DelayedCancellationResponse() =>
        Json(new { Message = "This subscription will be canceled at the end of the current period." });

    /// <summary>The provider's validation-failure shape: a flat list of messages under <c>errors</c>.</summary>
    public static string ValidationErrors(params string[] errors) => Json(new { Errors = errors });

    /// <summary>The provider's simpler failure shape: a single message under <c>error</c>.</summary>
    public static string SingleError(string message) => Json(new { Error = message });

    /// <summary>
    /// The customer-validation failure shape the SDK's generated model expects — an object of keyed
    /// message lists rather than the flat list every other operation returns.
    /// </summary>
    public static string CustomerValidationErrors(params string[] errors) =>
        Json(new { Errors = new { PerPage = errors } });
}
