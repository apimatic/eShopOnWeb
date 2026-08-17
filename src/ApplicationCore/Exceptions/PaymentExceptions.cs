using System;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

/// <summary>A referenced entity (order, payment, saved card) does not exist for the caller. Maps to 404.</summary>
public class NotFoundException : Exception
{
    public NotFoundException(string message) : base(message) { }
}

/// <summary>The request is well-formed but not allowed in the current state (bad transition, over-refund). Maps to 409.</summary>
public class PaymentConflictException : Exception
{
    public PaymentConflictException(string message) : base(message) { }
}

/// <summary>The request itself is invalid (e.g. no items, no card and no saved card). Maps to 400.</summary>
public class PaymentValidationException : Exception
{
    public PaymentValidationException(string message) : base(message) { }
}

/// <summary>
/// PayPal answered a card payment with a challenge that requires a shopper to approve in a browser.
/// Per the integration mandate this is surfaced, not worked around, so an operator can act on it. Maps to 422.
/// </summary>
public class PayPalChallengeRequiredException : Exception
{
    public PayPalChallengeRequiredException(string message) : base(message) { }
}

/// <summary>A PayPal API call failed. Carries PayPal's debug id and issue for support/diagnosis. Maps to 502.</summary>
public class PayPalApiException : Exception
{
    public PayPalApiException(string message, int httpStatus, string? debugId, string? issue)
        : base(message)
    {
        HttpStatus = httpStatus;
        DebugId = debugId;
        Issue = issue;
    }

    public int HttpStatus { get; }
    public string? DebugId { get; }
    public string? Issue { get; }
}
