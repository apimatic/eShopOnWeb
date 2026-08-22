using System;
using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;

namespace Microsoft.eShopWeb.PublicApi;

internal static class EndpointIdentity
{
    public static string? GetBuyerId(this ClaimsPrincipal user) => user.Identity?.Name;

    public static IResult ToHttpResult(this Exception exception)
    {
        return exception switch
        {
            UnusablePhoneNumberException ex => Results.BadRequest(new { message = ex.Message }),
            ContactNumberNotFoundException ex => Results.NotFound(new { message = ex.Message }),
            ShopperOrderNotFoundException ex => Results.NotFound(new { message = ex.Message }),
            NotificationNotFoundException ex => Results.NotFound(new { message = ex.Message }),
            NotificationResendNotAllowedException ex => Results.BadRequest(new { message = ex.Message }),
            EmptyBasketOnCheckoutException ex => Results.BadRequest(new { message = ex.Message }),
            InvalidOperationException ex => Results.BadRequest(new { message = ex.Message }),
            SmsProviderUnavailableException ex => Results.Json(new { message = ex.Message }, statusCode: 502),
            _ => throw exception
        };
    }
}
