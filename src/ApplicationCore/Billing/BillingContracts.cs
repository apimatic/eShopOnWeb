using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.ApplicationCore.Billing;

public sealed record BillingUser(string Id, string Email, string FirstName, string LastName);

public sealed record SubscriptionPlan(
    string ProductHandle,
    string Name,
    string? Description,
    long PriceInCents,
    int Interval,
    string IntervalUnit);

public sealed record SubscriptionDetails(
    int? SubscriptionId,
    string Reference,
    string ProductHandle,
    string ProductName,
    long? PriceInCents,
    string? Currency,
    string State,
    DateTimeOffset? NextBillingDate,
    DateTimeOffset? NextAssessmentAt,
    bool InProgress = false);

public interface ISubscriptionBillingService
{
    Task<IReadOnlyList<SubscriptionPlan>> ListPlansAsync(CancellationToken cancellationToken);
    Task<SubscriptionDetails> SubscribeAsync(BillingUser user, string productHandle, CancellationToken cancellationToken);
    Task<IReadOnlyList<SubscriptionDetails>> ListSubscriptionsAsync(BillingUser user, CancellationToken cancellationToken);
}

public sealed class BillingValidationException : Exception
{
    public BillingValidationException(string message) : base(message) { }
}

public sealed class BillingProviderException : Exception
{
    public BillingProviderException(string message, int? providerStatusCode = null, Exception? innerException = null)
        : base(message, innerException)
    {
        ProviderStatusCode = providerStatusCode;
    }

    public int? ProviderStatusCode { get; }
}
