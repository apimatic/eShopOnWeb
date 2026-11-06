using Microsoft.AspNetCore.Http;
using Microsoft.eShopWeb.PublicApi.Subscriptions;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public static class BillingExceptionExtensions
{
    /// <summary>One failure-kind to status mapping for every subscription endpoint.</summary>
    public static IResult ToResult(this BillingException ex) =>
        Results.Json(new { statusCode = ex.StatusCode, message = ex.Message }, statusCode: ex.StatusCode);
}
