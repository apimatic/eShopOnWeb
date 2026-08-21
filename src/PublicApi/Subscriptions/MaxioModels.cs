using System;

namespace Microsoft.eShopWeb.PublicApi.Subscriptions;

public sealed class MaxioProduct
{
    public int Id { get; init; }
    public string Name { get; init; } = string.Empty;
    public string Handle { get; init; } = string.Empty;
    public string? Description { get; init; }
    public long PriceInCents { get; init; }
    public int Interval { get; init; }
    public string IntervalUnit { get; init; } = string.Empty;
    public bool RequireCreditCard { get; init; }
    public DateTimeOffset? ArchivedAt { get; init; }
    public string ProductFamilyHandle { get; init; } = string.Empty;
}

public sealed class MaxioCustomer
{
    public int Id { get; init; }
    public string Reference { get; init; } = string.Empty;
}

public sealed class MaxioSubscription
{
    public int Id { get; init; }
    public string State { get; init; } = string.Empty;
    public long ProductPriceInCents { get; init; }
    public DateTimeOffset? CurrentPeriodEndsAt { get; init; }
    public DateTimeOffset? NextAssessmentAt { get; init; }
    public string? Reference { get; init; }
    public MaxioProduct? Product { get; init; }
}

public sealed class CreateMaxioCustomer
{
    public string FirstName { get; init; } = string.Empty;
    public string LastName { get; init; } = string.Empty;
    public string Email { get; init; } = string.Empty;
    public string Reference { get; init; } = string.Empty;
    public string UniquenessToken { get; init; } = string.Empty;
}

public sealed class CreateMaxioSubscription
{
    public string ProductHandle { get; init; } = string.Empty;
    public int CustomerId { get; init; }
    public string Reference { get; init; } = string.Empty;
    public string PaymentCollectionMethod { get; init; } = "remittance";
    public string UniquenessToken { get; init; } = string.Empty;
}
