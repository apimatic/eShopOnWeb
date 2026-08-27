using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.PublicApi.Subscriptions;

public sealed record SubscriptionPlanDto(
    string Handle,
    string Name,
    string? Description,
    long? PriceInCents,
    int? Interval,
    string? IntervalUnit);

public sealed record SubscriptionDto(
    int? Id,
    string ProductHandle,
    string ProductName,
    long? PriceInCents,
    string? State,
    DateTimeOffset? NextBillingDate);

public sealed record SubscribeRequest(string ProductHandle);

public sealed record BillingUser(
    string Id,
    string Email,
    string FirstName,
    string LastName);

public interface ISubscriptionBillingService
{
    Task<IReadOnlyList<SubscriptionPlanDto>> ListPlansAsync(CancellationToken cancellationToken);
    Task<SubscriptionDto> SubscribeAsync(BillingUser user, string productHandle, CancellationToken cancellationToken);
    Task<IReadOnlyList<SubscriptionDto>> ListForUserAsync(BillingUser user, CancellationToken cancellationToken);
}

public sealed class MaxioIntegrationException : Exception
{
    public MaxioIntegrationException(int statusCode, string code, string safeMessage, Exception? innerException = null)
        : base(safeMessage, innerException)
    {
        StatusCode = statusCode;
        Code = code;
    }

    public int StatusCode { get; }
    public string Code { get; }
}

public sealed class MaxioAmbiguousWriteException : Exception
{
    public MaxioAmbiguousWriteException(Exception innerException)
        : base("The Maxio write may have succeeded, but its outcome could not be confirmed.", innerException)
    {
    }
}
