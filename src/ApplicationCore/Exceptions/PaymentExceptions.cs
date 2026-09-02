using System;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

/// <summary>Base class for payment integration failures.</summary>
public class PaymentException : Exception
{
    public PaymentException(string message) : base(message) { }
    public PaymentException(string message, Exception innerException) : base(message, innerException) { }
}

/// <summary>A PayPal API call failed. Carries PayPal's error name and debug id; never carries card data.</summary>
public class PayPalApiException : PaymentException
{
    public PayPalApiException(System.Net.HttpStatusCode statusCode, string? errorName, string message, string? debugId)
        : base(debugId is null ? message : $"{message} (PayPal error: {errorName}, debug id: {debugId})")
    {
        StatusCode = statusCode;
        ErrorName = errorName;
        DebugId = debugId;
    }

    public System.Net.HttpStatusCode StatusCode { get; }
    public string? ErrorName { get; }
    public string? DebugId { get; }
}

/// <summary>PayPal declined the card or the authorization failed.</summary>
public class PaymentDeclinedException : PaymentException
{
    public PaymentDeclinedException(string message) : base(message) { }
    public PaymentDeclinedException(string message, Exception innerException) : base(message, innerException) { }
}

/// <summary>
/// The PayPal authorization went stale and could not be reauthorized.
/// The operator must ask the shopper to pay again (or cancel the order).
/// </summary>
public class AuthorizationRenewalException : PaymentException
{
    public AuthorizationRenewalException(string message) : base(message) { }
    public AuthorizationRenewalException(string message, Exception innerException) : base(message, innerException) { }
}

/// <summary>PayPal requires a shopper verification step (e.g. 3-D Secure) that this integration does not perform.</summary>
public class PaymentVerificationRequiredException : PaymentException
{
    public PaymentVerificationRequiredException(string message) : base(message) { }
}

/// <summary>The requested payment state transition is not valid for the current state.</summary>
public class InvalidPaymentStateException : PaymentException
{
    public InvalidPaymentStateException(string message) : base(message) { }
}
