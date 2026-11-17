using System;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

/// <summary>Invalid input supplied by an API caller (HTTP 400).</summary>
public class PaymentValidationException : Exception
{
    public PaymentValidationException(string message) : base(message) { }
}

/// <summary>A requested payment resource does not exist for this caller (HTTP 404).</summary>
public class PaymentNotFoundException : Exception
{
    public PaymentNotFoundException(string message) : base(message) { }
}

/// <summary>The caller may not act on this resource (HTTP 403).</summary>
public class PaymentForbiddenException : Exception
{
    public PaymentForbiddenException(string message) : base(message) { }
}

/// <summary>The operation conflicts with the current payment state (HTTP 409).</summary>
public class PaymentConflictException : Exception
{
    public PaymentConflictException(string message) : base(message) { }
}

/// <summary>The processor declined the payment (HTTP 402).</summary>
public class PaymentDeclinedException : Exception
{
    public PaymentDeclinedException(string message) : base(message) { }
}

/// <summary>Finix rejected or failed a request (HTTP 502).</summary>
public class PaymentGatewayException : Exception
{
    public PaymentGatewayException(string message, string? code = null, string? relatedResourceId = null)
        : base(message)
    {
        Code = code;
        RelatedResourceId = relatedResourceId;
    }

    public string? Code { get; }

    /// <summary>
    /// The id Finix reported for the resource that caused a conflict (for
    /// example the existing authorization when an idempotency key is reused).
    /// </summary>
    public string? RelatedResourceId { get; }
}

/// <summary>Finix settings are missing or invalid (HTTP 500).</summary>
public class PaymentConfigurationException : Exception
{
    public PaymentConfigurationException(string message) : base(message) { }
}
