using System;

namespace Microsoft.eShopWeb.PublicApi.Subscriptions;

public sealed record MaxioProductFamily(long Id, string? Handle, string? Name, DateTime? ArchivedAt);

public sealed record MaxioProduct(
    long Id,
    string? Name,
    string? Handle,
    string? Description,
    int? PriceInCents,
    int? Interval,
    string? IntervalUnit,
    bool? RequireCreditCard,
    bool? Taxable,
    DateTime? ArchivedAt,
    MaxioProductFamily? ProductFamily);

public sealed record MaxioCustomer(
    long Id,
    string? Reference,
    string? FirstName,
    string? LastName,
    string? Email);

public sealed record MaxioSubscription(
    long Id,
    string? State,
    string? Reference,
    string? Currency,
    int? ProductPriceInCents,
    long? BalanceInCents,
    DateTime? ActivatedAt,
    DateTime? CreatedAt,
    DateTime? CurrentPeriodEndsAt,
    DateTime? CanceledAt,
    DateTime? ExpiresAt,
    MaxioProduct? Product,
    MaxioCustomer? Customer)
{
    public bool IsTerminalState =>
        string.Equals(State, "canceled", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(State, "expired", StringComparison.OrdinalIgnoreCase);
}

internal sealed record MaxioProductFamilyEnvelope(MaxioProductFamily? ProductFamily);

internal sealed record MaxioProductEnvelope(MaxioProduct? Product);

internal sealed record MaxioCustomerEnvelope(MaxioCustomer? Customer);

internal sealed record MaxioSubscriptionEnvelope(MaxioSubscription? Subscription);

internal sealed record MaxioCustomerAttributes(string? FirstName, string? LastName, string? Email, string? Reference);

internal sealed record MaxioCreateCustomerRequest(MaxioCustomerAttributes Customer, Guid? UniquenessToken);

internal sealed record MaxioCreateSubscriptionAttributes(
    string? ProductHandle,
    long? CustomerId,
    string? PaymentCollectionMethod,
    string? Reference);

internal sealed record MaxioCreateSubscriptionRequest(MaxioCreateSubscriptionAttributes Subscription, Guid? UniquenessToken);

internal sealed record MaxioErrorEnvelope(string[]? Errors);
