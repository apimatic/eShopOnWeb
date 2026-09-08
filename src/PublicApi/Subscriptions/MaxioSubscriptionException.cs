using System;
using Microsoft.AspNetCore.Http;

namespace Microsoft.eShopWeb.PublicApi.Subscriptions;

/// <summary>
/// Application-level failure raised at the Maxio integration boundary.
/// Carries the HTTP status the PublicApi error middleware should reply with and a
/// caller-safe message. Never wraps raw SDK/framework exception text in <see cref="Message"/>.
/// </summary>
public sealed class MaxioSubscriptionException : Exception
{
    public int StatusCode { get; }

    public MaxioSubscriptionException(int statusCode, string message, Exception? innerException = null)
        : base(message, innerException)
    {
        StatusCode = statusCode;
    }

    /// <summary>The Maxio settings are missing/blank on this host.</summary>
    public static MaxioSubscriptionException NotConfigured() =>
        new(StatusCodes.Status500InternalServerError, "Maxio billing is not configured on this server.");

    /// <summary>The billing provider could not be reached (connection/timeout).</summary>
    public static MaxioSubscriptionException ProviderUnavailable(string operation, Exception? innerException = null) =>
        new(StatusCodes.Status503ServiceUnavailable,
            $"The billing provider could not be reached while trying to {operation}. Please retry shortly.", innerException);

    /// <summary>The billing provider returned a 5xx response.</summary>
    public static MaxioSubscriptionException ProviderError(string operation, Exception? innerException = null) =>
        new(StatusCodes.Status502BadGateway,
            $"The billing provider reported an error while trying to {operation}. Please retry shortly.", innerException);

    /// <summary>The billing provider returned a body that could not be processed (outcome unknown).</summary>
    public static MaxioSubscriptionException UnreadableResponse(string operation, Exception? innerException = null) =>
        new(StatusCodes.Status502BadGateway,
            $"The billing provider returned an unreadable response while trying to {operation}. Please retry shortly.", innerException);

    /// <summary>The billing provider rejected the request (4xx); detail is caller-safe provider text.</summary>
    public static MaxioSubscriptionException ProviderRejected(int statusCode, string detail, Exception? innerException = null) =>
        new(statusCode, detail, innerException);

    public static MaxioSubscriptionException PlanNotFound(string productHandle) =>
        new(StatusCodes.Status404NotFound, $"The subscription plan '{productHandle}' was not found.");

    public static MaxioSubscriptionException BadRequest(string message) =>
        new(StatusCodes.Status400BadRequest, message);
}
