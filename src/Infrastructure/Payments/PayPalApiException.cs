using System;
using System.Net;

namespace Microsoft.eShopWeb.Infrastructure.Payments;

public class PayPalApiException : Exception
{
    public PayPalApiException(HttpStatusCode statusCode, string name, string? issue, string? debugId)
        : base(BuildSafeMessage(statusCode, name, issue, debugId))
    {
        StatusCode = statusCode;
        ErrorName = name;
        Issue = issue;
        DebugId = debugId;
    }

    public HttpStatusCode StatusCode { get; }
    public string ErrorName { get; }
    public string? Issue { get; }
    public string? DebugId { get; }

    public bool IsTransient =>
        StatusCode is HttpStatusCode.RequestTimeout or HttpStatusCode.TooManyRequests || (int)StatusCode >= 500;

    public bool IsReauthorizationUnavailable =>
        Issue is "REAUTHORIZE_NOT_ALLOWED" or "AUTHORIZATION_EXPIRED" or "AUTHORIZATION_ALREADY_COMPLETED" or "MAX_NUMBER_OF_REAUTHORIZATIONS_EXCEEDED";

    private static string BuildSafeMessage(HttpStatusCode statusCode, string name, string? issue, string? debugId)
    {
        var detail = issue ?? name;
        var trace = string.IsNullOrWhiteSpace(debugId) ? string.Empty : $" PayPal debug ID: {debugId}.";
        return $"PayPal rejected the operation ({(int)statusCode}, {detail}).{trace}";
    }
}

public sealed class PayPalPayerActionRequiredException : Exception
{
    public PayPalPayerActionRequiredException()
        : base("PayPal requires an interactive cardholder challenge. This API supports headless direct-card payments only; use another card.")
    {
    }
}
