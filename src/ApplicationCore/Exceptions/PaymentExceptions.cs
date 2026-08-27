using System;
using System.Net;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

/// <summary>A resource the caller asked for does not exist (or belongs to someone else).</summary>
public class ResourceNotFoundException : Exception
{
    public ResourceNotFoundException(string message) : base(message) { }
}

/// <summary>The requested payment action conflicts with the current state of the order/payment.</summary>
public class PaymentConflictException : Exception
{
    public PaymentConflictException(string message) : base(message) { }
}

/// <summary>The payment processor declined the payment or requires an action we cannot complete headlessly.</summary>
public class PaymentDeclinedException : Exception
{
    public PaymentDeclinedException(string message) : base(message) { }
}

/// <summary>The payment processor rejected or failed a call. Carries the processor's error detail.</summary>
public class PaymentGatewayException : Exception
{
    public PaymentGatewayException(string message, HttpStatusCode? statusCode = null, string? errorName = null)
        : base(message)
    {
        StatusCode = statusCode;
        ErrorName = errorName;
    }

    public HttpStatusCode? StatusCode { get; }
    public string? ErrorName { get; }
}
