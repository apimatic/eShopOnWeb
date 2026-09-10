using System.Net;
using Microsoft.AspNetCore.Http;
using Microsoft.eShopWeb.ApplicationCore.Subscriptions;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>Translates subscription-domain failures into API responses with sensible status codes.</summary>
internal static class SubscriptionErrorResults
{
    /// <summary>
    /// Maps a Maxio failure. Client-caused validation errors (422) surface as 400 Bad Request;
    /// everything else is an upstream failure surfaced as 502 Bad Gateway.
    /// </summary>
    public static IResult FromMaxio(MaxioApiException exception)
    {
        var statusCode = exception.StatusCode == (int)HttpStatusCode.UnprocessableEntity
            ? StatusCodes.Status400BadRequest
            : StatusCodes.Status502BadGateway;

        var title = statusCode == StatusCodes.Status400BadRequest
            ? "The billing provider rejected the request."
            : "The billing provider is currently unavailable.";

        return Results.Problem(
            title: title,
            detail: string.Join("; ", exception.Errors),
            statusCode: statusCode);
    }
}
