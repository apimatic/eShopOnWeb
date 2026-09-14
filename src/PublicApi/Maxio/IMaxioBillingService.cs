using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.PublicApi.Maxio;

public sealed record MaxioUser(string Id, string Email, string FirstName, string LastName);

public sealed record SubscriptionPlan(
    string Handle,
    string Name,
    string? Description,
    long PriceInCents,
    int Interval,
    string IntervalUnit,
    bool RequiresPaymentMethod);

public sealed record UserSubscription(
    int Id,
    string ProductHandle,
    string ProductName,
    long PriceInCents,
    int Interval,
    string IntervalUnit,
    string State,
    DateTimeOffset? NextBillingDate);

public interface IMaxioBillingService
{
    Task<IReadOnlyList<SubscriptionPlan>> GetPlansAsync(CancellationToken cancellationToken);
    Task<UserSubscription?> SubscribeAsync(MaxioUser user, string productHandle, CancellationToken cancellationToken);
    Task<IReadOnlyList<UserSubscription>> GetSubscriptionsAsync(string userId, CancellationToken cancellationToken);
}

public sealed class MaxioApiException : Exception
{
    public MaxioApiException(string operation, int statusCode)
        : base($"Maxio rejected the {operation} request with HTTP status {statusCode}.")
    {
        StatusCode = statusCode;
    }

    public int StatusCode { get; }
}
