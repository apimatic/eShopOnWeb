using Microsoft.AspNetCore.Http;
using Microsoft.eShopWeb.PublicApi.Maxio;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// Maps the billing boundary's failure kinds onto HTTP responses: the provider's own
/// 4xx (caller's fault) passes its status through; everything else is a 502 with a
/// caller-safe message — never an SDK exception message.
/// </summary>
public static class MaxioEndpointResults
{
    public static IResult Failure(MaxioBillingException ex)
    {
        return ex.Kind switch
        {
            MaxioBillingException.FailureKind.RequestRejected
                => Results.Json(new { error = ex.Message }, statusCode: ex.ProviderStatusCode ?? 400),
            MaxioBillingException.FailureKind.UnknownOutcome
                => Results.Json(new { error = ex.Message }, statusCode: 502),
            _
                => Results.Json(new { error = ex.Message }, statusCode: 502)
        };
    }
}