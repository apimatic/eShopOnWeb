using System;

namespace Microsoft.eShopWeb.ApplicationCore.PayPal;

/// <summary>Raised when a PayPal API call fails. Carries PayPal's raw body and debug id for support.</summary>
public class PayPalApiException : Exception
{
    public PayPalApiException(string message, string? responseBody = null, string? debugId = null, int? statusCode = null)
        : base(message)
    {
        ResponseBody = responseBody;
        DebugId = debugId;
        StatusCode = statusCode;
    }

    public string? ResponseBody { get; }
    public string? DebugId { get; }
    public int? StatusCode { get; }

    /// <summary>The first PayPal issue name in the response body, if one could be parsed.</summary>
    public string? IssueName { get; init; }
}

/// <summary>
/// Raised when PayPal answers a card payment with a challenge that requires the shopper to approve
/// in a browser (e.g. <c>PAYER_ACTION_REQUIRED</c>). Per the task, we stop rather than build an
/// approval round-trip.
/// </summary>
public class PayPalChallengeRequiredException : Exception
{
    public PayPalChallengeRequiredException(string message) : base(message) { }
}
