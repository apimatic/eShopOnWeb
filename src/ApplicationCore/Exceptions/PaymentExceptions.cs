using System;
using System.Collections.Generic;
using System.Linq;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

/// <summary>
/// Base for domain/integration exceptions that map to a specific HTTP status code.
/// The PublicApi exception middleware reads <see cref="StatusCode"/> to shape the response.
/// </summary>
public abstract class ApiException : Exception
{
    protected ApiException(string message, int statusCode) : base(message)
    {
        StatusCode = statusCode;
    }

    protected ApiException(string message, int statusCode, Exception innerException) : base(message, innerException)
    {
        StatusCode = statusCode;
    }

    /// <summary>The HTTP status code that best represents this failure.</summary>
    public int StatusCode { get; }
}

/// <summary>A requested entity was not found — or is not visible to the caller (404).</summary>
public class EntityNotFoundException : ApiException
{
    public EntityNotFoundException(string message) : base(message, 404) { }

    public EntityNotFoundException(string entity, object id)
        : base($"{entity} '{id}' was not found.", 404) { }
}

/// <summary>The request was malformed or failed validation (400).</summary>
public class PaymentValidationException : ApiException
{
    public PaymentValidationException(string message) : base(message, 400) { }
}

/// <summary>The operation is not valid for the resource's current state (409).</summary>
public class PaymentConflictException : ApiException
{
    public PaymentConflictException(string message) : base(message, 409) { }
}

/// <summary>The card payment was declined by PayPal or the issuer (402).</summary>
public class PaymentDeclinedException : ApiException
{
    public PaymentDeclinedException(string message) : base(message, 402) { }
}

/// <summary>
/// PayPal returned a challenge that requires the shopper to approve in a browser
/// (e.g. 3-D Secure / PAYER_ACTION_REQUIRED). This integration does not build an
/// approval round-trip; it surfaces the condition so an operator can act on it (409).
/// </summary>
public class PaymentApprovalRequiredException : ApiException
{
    public PaymentApprovalRequiredException(string message) : base(message, 409) { }
}

/// <summary>
/// A stale authorization could not be renewed (PayPal only allows reauthorization on
/// days 4-29; after 30 days a brand-new authorization is required). Phrased so an
/// operator knows the concrete next step (422).
/// </summary>
public class AuthorizationCannotBeRenewedException : ApiException
{
    public AuthorizationCannotBeRenewedException(string message) : base(message, 422) { }
}

/// <summary>
/// PayPal responded with an error we cannot recover from. Carries PayPal's debug id so
/// the failure can be traced with PayPal support (502 Bad Gateway — upstream failure).
/// </summary>
public class PayPalApiException : ApiException
{
    public PayPalApiException(string message, int payPalStatusCode, string? debugId = null, IReadOnlyList<string>? issues = null)
        : base(message, 502)
    {
        PayPalStatusCode = payPalStatusCode;
        DebugId = debugId;
        Issues = issues ?? Array.Empty<string>();
    }

    /// <summary>The HTTP status code PayPal returned.</summary>
    public int PayPalStatusCode { get; }

    /// <summary>PayPal's debug id from the error response, for support traceability.</summary>
    public string? DebugId { get; }

    /// <summary>The PayPal issue codes from the error response (e.g. AUTHORIZATION_EXPIRED).</summary>
    public IReadOnlyList<string> Issues { get; }

    public bool HasIssue(string issue) =>
        Issues.Any(i => string.Equals(i, issue, StringComparison.OrdinalIgnoreCase));
}
