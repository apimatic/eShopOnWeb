using System.Collections.Generic;
using System;
using System.Net;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public interface IMaxioBillingService
{
    Task<IReadOnlyList<SubscriptionPlanResponse>> ListPlansAsync(CancellationToken cancellationToken);
    Task<SubscriptionResponse> SubscribeAsync(MaxioShopper shopper, string planHandle, CancellationToken cancellationToken);
    Task<IReadOnlyList<SubscriptionResponse>> ListSubscriptionsAsync(MaxioShopper shopper, CancellationToken cancellationToken);
}

public sealed record MaxioShopper(string UserId, string Email, string FirstName, string LastName);

public sealed class MaxioApiException : Exception
{
    public HttpStatusCode StatusCode { get; }
    public MaxioApiException(HttpStatusCode statusCode, string message) : base(message) => StatusCode = statusCode;
}
