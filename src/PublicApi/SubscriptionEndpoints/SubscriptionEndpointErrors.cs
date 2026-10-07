using Microsoft.AspNetCore.Http;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// Maps billing failures to client-facing responses: a provider 4xx keeps its
/// status class (the caller can act on it); transport failures and unreadable
/// responses (no provider status) surface as 502. Messages are the
/// caller-safe ones carried by <see cref="MaxioBillingException"/>.
/// </summary>
public static class SubscriptionEndpointErrors
{
    public static IResult ToResult(MaxioBillingException exception) => exception.ProviderStatusCode switch
    {
        404 => Results.NotFound(new { message = exception.Message }),
        400 => Results.BadRequest(new { message = exception.Message }),
        422 => Results.UnprocessableEntity(new { message = exception.Message }),
        409 => Results.Conflict(new { message = exception.Message }),
        401 => Results.Unauthorized(),
        _ => Results.Json(new { message = exception.Message }, statusCode: StatusCodes.Status502BadGateway)
    };
}
