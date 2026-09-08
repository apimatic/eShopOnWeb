using System;
using System.Security.Claims;
using BlazorShared.Models;
using Microsoft.AspNetCore.Http;
using Microsoft.eShopWeb.PublicApi.Maxio;
using Microsoft.Extensions.Logging;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>Shared helpers for the subscription endpoints.</summary>
internal static class SubscriptionEndpointSupport
{
    /// <summary>
    /// The authenticated caller's identity is carried on the JWT as the Name claim
    /// (the eShop user name, which is their email address). See IdentityTokenClaimService.
    /// </summary>
    public static string? GetUserEmail(ClaimsPrincipal principal) =>
        principal.Identity?.IsAuthenticated == true ? principal.Identity.Name : null;

    public static IResult BadRequest(string message) =>
        Results.BadRequest(new ErrorDetails { StatusCode = StatusCodes.Status400BadRequest, Message = message });

    public static IResult MapMaxioFailure(Exception exception, ILogger logger)
    {
        switch (exception)
        {
            case MaxioApiException apiException when (int)apiException.StatusCode is 401 or 403 or >= 500:
                logger.LogError(apiException, "Maxio API rejected a request with {StatusCode}.", apiException.StatusCode);
                return Results.Json(
                    new ErrorDetails
                    {
                        StatusCode = StatusCodes.Status502BadGateway,
                        Message = "The billing provider could not complete the request.",
                    },
                    statusCode: StatusCodes.Status502BadGateway);

            case MaxioApiException apiException:
                // 4xx from Maxio (e.g. unknown product handle, validation failure) is a caller error.
                logger.LogWarning(apiException, "Maxio API rejected a request with {StatusCode}.", apiException.StatusCode);
                return BadRequest(apiException.Message);

            case MaxioUnavailableException unavailableException:
                logger.LogError(unavailableException, "Maxio API is unavailable or not configured.");
                return Results.Json(
                    new ErrorDetails
                    {
                        StatusCode = StatusCodes.Status503ServiceUnavailable,
                        Message = unavailableException.Message,
                    },
                    statusCode: StatusCodes.Status503ServiceUnavailable);

            default:
                return Results.StatusCode(StatusCodes.Status500InternalServerError);
        }
    }
}
