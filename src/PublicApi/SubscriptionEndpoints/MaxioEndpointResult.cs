using System;
using BlazorShared.Models;
using Microsoft.AspNetCore.Http;
using Microsoft.eShopWeb.PublicApi.Maxio;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// Maps Maxio failures to HTTP error responses. The body mirrors the JSON shape the
/// PublicApi <see cref="Microsoft.eShopWeb.PublicApi.Middleware.ExceptionMiddleware"/>
/// already produces (<see cref="ErrorDetails"/>).
/// </summary>
internal static class MaxioEndpointResult
{
    /// <summary>
    /// Returns an error <see cref="IResult"/> for a known integration exception, or
    /// <c>null</c> when the exception should be re-thrown for the global middleware.
    /// </summary>
    public static IResult? TryMapError(Exception exception) => exception switch
    {
        MaxioValidationException validation =>
            Error(StatusCodes.Status400BadRequest, validation.Message),

        MaxioConfigurationException configuration =>
            Error(StatusCodes.Status500InternalServerError, configuration.Message),

        MaxioApiException api =>
            Error(StatusCodes.Status502BadGateway, api.Message),

        _ => null
    };

    private static IResult Error(int statusCode, string message) =>
        Results.Json(new ErrorDetails { StatusCode = statusCode, Message = message }, statusCode: statusCode);
}
