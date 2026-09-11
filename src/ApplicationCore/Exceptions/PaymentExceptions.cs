using System;
using System.Collections.Generic;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

/// <summary>A requested entity (order, saved card, ...) could not be found. Maps to 404.</summary>
public class EntityNotFoundException : Exception
{
    public EntityNotFoundException(string message) : base(message) { }
}

/// <summary>The caller tried to see or act on data that is not theirs. Maps to 403.</summary>
public class ForbiddenActionException : Exception
{
    public ForbiddenActionException(string message) : base(message) { }
}

/// <summary>The action is not valid for the resource's current state. Maps to 409.</summary>
public class ConflictException : Exception
{
    public ConflictException(string message) : base(message) { }
}

/// <summary>Request data was invalid (bad amount, missing card, ...). Maps to 400.</summary>
public class InvalidRequestException : Exception
{
    public InvalidRequestException(string message) : base(message) { }
}

/// <summary>
/// A payment operation could not be completed for a business reason (card declined, hold could
/// not be renewed, refund exceeds captured amount, ...). Carries an operator-actionable message.
/// Maps to 422.
/// </summary>
public class PaymentException : Exception
{
    public PaymentException(string message) : base(message) { }
}

/// <summary>
/// PayPal answered a card payment with a challenge that requires the shopper to approve in a
/// browser. Per the integration scope we do not build an approval round-trip; we stop and
/// surface this so an operator can act. Maps to 422.
/// </summary>
public class PaymentChallengeRequiredException : PaymentException
{
    public PaymentChallengeRequiredException(string message) : base(message) { }
}

/// <summary>
/// An error returned by the PayPal API itself (non-2xx). Preserves the upstream status, error
/// name and debug id (never any card data) for diagnostics. Maps to 502.
/// </summary>
public class PayPalApiException : Exception
{
    public PayPalApiException(int statusCode, string? name, string message, string? debugId, IReadOnlyList<string>? details = null)
        : base(message)
    {
        StatusCode = statusCode;
        Name = name;
        DebugId = debugId;
        Details = details ?? new List<string>();
    }

    public int StatusCode { get; }
    public string? Name { get; }
    public string? DebugId { get; }
    public IReadOnlyList<string> Details { get; }
}
