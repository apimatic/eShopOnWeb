using System;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

/// <summary>A requested resource (order, saved card) does not exist or is not owned by the caller.</summary>
public class PaymentNotFoundException : Exception
{
    public PaymentNotFoundException(string message) : base(message) { }
}

/// <summary>The operation is not valid for the resource's current state (e.g. cancelling a fulfilled order).</summary>
public class PaymentConflictException : Exception
{
    public PaymentConflictException(string message) : base(message) { }
}

/// <summary>The request was well-formed but failed a business rule (e.g. refunding more than was captured).</summary>
public class PaymentValidationException : Exception
{
    public PaymentValidationException(string message) : base(message) { }
}
