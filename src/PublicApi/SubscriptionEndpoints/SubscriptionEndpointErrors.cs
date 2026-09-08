using System.Net.Http;
using System.Text.Json;
using BlazorShared.Models;
using Microsoft.AspNetCore.Http;
using Microsoft.eShopWeb.PublicApi.Maxio;
using Microsoft.eShopWeb.PublicApi.SubscriptionServices;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// Renders expected subscription-domain and Maxio-upstream failures as JSON error responses in the
/// same {StatusCode, Message} shape the rest of PublicApi uses. Unexpected exceptions are left to
/// the global exception middleware.
/// </summary>
internal static class SubscriptionEndpointErrors
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNamingPolicy = null
    };

    public static IResult Unauthorized() => Results.Json(
        new ErrorDetails { StatusCode = StatusCodes.Status401Unauthorized, Message = "A valid bearer token is required." },
        SerializerOptions,
        statusCode: StatusCodes.Status401Unauthorized);

    public static IResult BadRequest(string message) => Results.Json(
        new ErrorDetails { StatusCode = StatusCodes.Status400BadRequest, Message = message },
        SerializerOptions,
        statusCode: StatusCodes.Status400BadRequest);

    public static IResult UnknownPlan(SubscriptionPlanNotFoundException ex) => Results.Json(
        new ErrorDetails { StatusCode = StatusCodes.Status400BadRequest, Message = ex.Message },
        SerializerOptions,
        statusCode: StatusCodes.Status400BadRequest);

    public static IResult NotConfigured(MaxioConfigurationException ex) => Results.Json(
        new ErrorDetails
        {
            StatusCode = StatusCodes.Status503ServiceUnavailable,
            Message = $"The Maxio billing service is not configured: {ex.Message}"
        },
        SerializerOptions,
        statusCode: StatusCodes.Status503ServiceUnavailable);

    public static IResult MaxioFailure(MaxioApiException ex) => Results.Json(
        new ErrorDetails
        {
            StatusCode = StatusCodes.Status502BadGateway,
            Message = $"The Maxio billing service returned an error (HTTP {ex.StatusCode})."
        },
        SerializerOptions,
        statusCode: StatusCodes.Status502BadGateway);

    public static IResult MaxioUnreachable(HttpRequestException ex) => Results.Json(
        new ErrorDetails
        {
            StatusCode = StatusCodes.Status502BadGateway,
            Message = "The Maxio billing service could not be reached."
        },
        SerializerOptions,
        statusCode: StatusCodes.Status502BadGateway);

    public static IResult MaxioTimeout() => Results.Json(
        new ErrorDetails
        {
            StatusCode = StatusCodes.Status504GatewayTimeout,
            Message = "The Maxio billing service timed out."
        },
        SerializerOptions,
        statusCode: StatusCodes.Status504GatewayTimeout);
}
