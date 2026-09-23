using System.Net;
using Microsoft.AspNetCore.Http;
using Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints.Maxio;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// Maps a <see cref="MaxioIntegrationException"/> to a caller-facing HTTP result, in one place so every
/// subscription endpoint answers the same failure the same way. Only the exception's caller-safe message
/// is surfaced — never an SDK or framework type name.
/// </summary>
public static class SubscriptionResults
{
    public static IResult FromError(MaxioIntegrationException ex)
    {
        if (ex.IsCallerError)
        {
            // The provider rejected the caller's request; hand back an actionable client status.
            var status = ex.StatusCode.HasValue ? (int)ex.StatusCode.Value : StatusCodes.Status400BadRequest;
            return Results.Problem(detail: ex.Message, statusCode: status);
        }

        // Our credentials/quota, transport, or a provider 5xx — none of which the caller can fix.
        var upstream = ex.StatusCode == HttpStatusCode.TooManyRequests
            ? StatusCodes.Status503ServiceUnavailable
            : StatusCodes.Status502BadGateway;
        return Results.Problem(detail: ex.Message, statusCode: upstream);
    }
}
