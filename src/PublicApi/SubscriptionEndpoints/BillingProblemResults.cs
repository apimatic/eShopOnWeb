using System.Net;
using Microsoft.AspNetCore.Http;
using Microsoft.eShopWeb.Infrastructure.Maxio;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// Maps a <see cref="MaxioBillingException"/> onto an HTTP problem response. A provider client-error
/// (4xx) surfaces as that same 4xx so the caller can act on it; provider 5xx and unknown failures
/// collapse to 502, an unreachable provider to 503, and our own misconfiguration to 500. The
/// exception message is already caller-safe (no SDK internals).
/// </summary>
internal static class BillingProblemResults
{
    public static IResult ToResult(MaxioBillingException exception)
    {
        int status = exception.StatusCode switch
        {
            null => StatusCodes.Status502BadGateway,
            HttpStatusCode.ServiceUnavailable => StatusCodes.Status503ServiceUnavailable,
            HttpStatusCode.InternalServerError => StatusCodes.Status500InternalServerError,
            var code when (int)code >= 500 => StatusCodes.Status502BadGateway,
            var code => (int)code
        };

        return Results.Problem(detail: exception.Message, statusCode: status);
    }
}
