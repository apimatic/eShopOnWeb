namespace Microsoft.eShopWeb.PublicApi.Subscriptions.Maxio.Dtos;

/// <summary>
/// Wire DTOs that mirror the Maxio Advanced Billing OpenAPI specification
/// (maxio-spec/openapi.yaml). The specification is the authoritative contract for
/// every Maxio interaction; these types are intentionally thin projections of the
/// schema shapes that this integration consumes.
///
/// Field mapping uses snake_case JSON property naming (STJ <c>SnakeCaseLower</c> policy),
/// matching the spec exactly. Date/time values are kept as strings because the spec
/// only requires a date-time format with no fixed timezone shape; they are parsed when
/// projected to the public API models.
/// </summary>

public class MaxioProductFamily
{
    public long Id { get; set; }
    public string? Name { get; set; }
    public string? Handle { get; set; }
    public string? Description { get; set; }
    public string? ArchivedAt { get; set; }
    public string? CreatedAt { get; set; }
    public string? UpdatedAt { get; set; }
}

public class MaxioProductFamilyEnvelope
{
    public MaxioProductFamily? ProductFamily { get; set; }
}

public class MaxioProduct
{
    public long Id { get; set; }
    public string? Name { get; set; }
    public string? Handle { get; set; }
    public string? Description { get; set; }
    public long PriceInCents { get; set; }
    public int Interval { get; set; }
    public string? IntervalUnit { get; set; }
    public long? TrialPriceInCents { get; set; }
    public int? TrialInterval { get; set; }
    public string? TrialIntervalUnit { get; set; }
    public bool? Taxable { get; set; }
    public bool? RequireCreditCard { get; set; }
    public string? ArchivedAt { get; set; }
    public string? CreatedAt { get; set; }
    public string? UpdatedAt { get; set; }
    public MaxioProductFamily? ProductFamily { get; set; }
}

public class MaxioProductEnvelope
{
    public MaxioProduct? Product { get; set; }
}

public class MaxioCustomer
{
    public long Id { get; set; }
    public string? FirstName { get; set; }
    public string? LastName { get; set; }
    public string? Email { get; set; }
    public string? Organization { get; set; }
    public string? Reference { get; set; }
    public string? CreatedAt { get; set; }
    public string? UpdatedAt { get; set; }
}

public class MaxioCustomerEnvelope
{
    public MaxioCustomer? Customer { get; set; }
}

/// <summary>
/// Body of POST /customers.json (spec: Create-Customer-Request).
/// </summary>
public class MaxioCreateCustomerEnvelope
{
    public MaxioCreateCustomer? Customer { get; set; }
}

public class MaxioCreateCustomer
{
    public string FirstName { get; set; } = string.Empty;
    public string LastName { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string? Organization { get; set; }
    public string? Reference { get; set; }
}

public class MaxioSubscription
{
    public long Id { get; set; }
    public string? State { get; set; }
    public string? CurrentPeriodEndsAt { get; set; }
    public string? NextAssessmentAt { get; set; }
    public string? CurrentPeriodStartedAt { get; set; }
    public string? ActivatedAt { get; set; }
    public string? CanceledAt { get; set; }
    public string? CreatedAt { get; set; }
    public string? UpdatedAt { get; set; }
    public string? ExpiresAt { get; set; }
    public string? TrialStartedAt { get; set; }
    public string? TrialEndedAt { get; set; }
    public long BalanceInCents { get; set; }
    public long ProductPriceInCents { get; set; }
    public string? Currency { get; set; }
    public string? PaymentCollectionMethod { get; set; }
    public bool? CancelAtEndOfPeriod { get; set; }
    public string? Reference { get; set; }
    public string? SignupRevenue { get; set; }
    public MaxioCustomer? Customer { get; set; }
    public MaxioProduct? Product { get; set; }
}

public class MaxioSubscriptionEnvelope
{
    public MaxioSubscription? Subscription { get; set; }
}

/// <summary>
/// Body of POST /subscriptions.json (spec: Create-Subscription-Request).
/// Only the fields this integration sends are modeled.
/// </summary>
public class MaxioCreateSubscriptionEnvelope
{
    public MaxioCreateSubscription? Subscription { get; set; }
}

public class MaxioCreateSubscription
{
    public string ProductHandle { get; set; } = string.Empty;
    public string? CustomerReference { get; set; }
    public string? PaymentCollectionMethod { get; set; }
}
