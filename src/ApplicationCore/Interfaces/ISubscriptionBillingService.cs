using System;
using System.Collections.Generic;
using System.Net;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

public interface ISubscriptionBillingService
{
    Task<IReadOnlyList<SubscriptionPlan>> GetPlansAsync(CancellationToken cancellationToken);
    Task<UserSubscription> SubscribeAsync(string userName, string? productHandle, CancellationToken cancellationToken);
    Task<IReadOnlyList<UserSubscription>> GetSubscriptionsAsync(string userName, CancellationToken cancellationToken);
}

public sealed record SubscriptionPlan(
    string Name,
    string Handle,
    string? Description,
    long? PriceInCents,
    int? Interval,
    string? IntervalUnit,
    string? PricePointHandle);

public sealed record UserSubscription(
    int Id,
    string? Reference,
    string? ProductName,
    string ProductHandle,
    long? PriceInCents,
    string? State,
    DateTimeOffset? NextBillingAt);

public class SubscriptionBillingException : Exception
{
    public SubscriptionBillingException(HttpStatusCode statusCode, string safeMessage, string code, Exception? innerException = null)
        : base(safeMessage, innerException)
    {
        StatusCode = statusCode;
        Code = code;
    }

    public HttpStatusCode StatusCode { get; }
    public string Code { get; }
}
