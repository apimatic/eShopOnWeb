using System.Net;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints.Maxio;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// Maps exceptions from the Maxio client onto HTTP responses.
/// </summary>
public static class MaxioEndpointResults
{
    /// <summary>
    /// Authorization policy that authenticates subscription endpoints with the
    /// JWT bearer scheme, so the caller's identity comes from the token rather
    /// than Identity's cookie scheme.
    /// </summary>
    public static AuthorizeAttribute JwtAuthorize() => new()
    {
        AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme
    };

    public static IResult FromMaxioException(MaxioApiException exception) =>
        exception.StatusCode switch
        {
            HttpStatusCode.NotFound => Results.NotFound(new { errors = exception.Errors }),
            HttpStatusCode.UnprocessableEntity => Results.Json(new { errors = exception.Errors }, statusCode: (int)HttpStatusCode.UnprocessableEntity),
            _ => Results.Problem(title: "Maxio Advanced Billing request failed",
                detail: string.Join("; ", exception.Errors), statusCode: (int)HttpStatusCode.BadGateway)
        };
}
