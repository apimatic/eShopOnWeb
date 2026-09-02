using System;
using System.Net;
using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;

namespace Microsoft.eShopWeb.PublicApi.Helpers;

public static class EndpointHelpers
{
    /// <summary>
    /// The buyer id is the username claim issued in the JWT (same value the Web
    /// storefront uses as BuyerId on baskets and orders).
    /// </summary>
    public static string? GetBuyerId(this ClaimsPrincipal user) => user.FindFirst(ClaimTypes.Name)?.Value;

    public static IResult MapException(Exception ex) => ex switch
    {
        OrderNotFoundException or SavedCardNotFoundException => Results.NotFound(new { message = ex.Message }),
        DuplicateException => Results.Conflict(new { message = ex.Message }),
        AuthorizationNotRenewableException => Results.Conflict(new { message = ex.Message }),
        PaymentGatewayException pgx => Results.Problem(
            title: "Payment gateway error",
            detail: pgx.Message + (pgx.DebugId is not null ? $" PayPal debug id: {pgx.DebugId}." : string.Empty),
            statusCode: (int)pgx.StatusCode >= 400 && (int)pgx.StatusCode < 500
                ? (int)HttpStatusCode.UnprocessableEntity
                : (int)HttpStatusCode.BadGateway),
        InvalidOperationException => Results.Conflict(new { message = ex.Message }),
        ArgumentException => Results.BadRequest(new { message = ex.Message }),
        _ => Results.Problem(detail: ex.Message, statusCode: (int)HttpStatusCode.InternalServerError)
    };
}
