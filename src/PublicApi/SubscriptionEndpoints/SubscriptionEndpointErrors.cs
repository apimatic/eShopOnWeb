using Microsoft.AspNetCore.Http;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

internal static class SubscriptionEndpointErrors
{
    public static IResult ToResult(Maxio.MaxioBillingException ex) =>
        Results.Json(new { statusCode = ex.StatusCode ?? 502, message = ex.Message }, statusCode: ex.StatusCode ?? 502);
}
