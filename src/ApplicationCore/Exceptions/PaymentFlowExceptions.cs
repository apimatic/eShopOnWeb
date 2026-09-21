using System;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

/// <summary>A requested resource (order, saved card, payment) does not exist. Maps to 404.</summary>
public class ResourceNotFoundException : Exception
{
    public ResourceNotFoundException(string message) : base(message) { }
}

/// <summary>The caller is not allowed to act on another shopper's data. Maps to 403.</summary>
public class ForbiddenException : Exception
{
    public ForbiddenException(string message) : base(message) { }
}

/// <summary>The operation is not valid for the payment's current state (e.g. fulfil before pay). Maps to 409.</summary>
public class PaymentStateException : Exception
{
    public PaymentStateException(string message) : base(message) { }
}

/// <summary>The card payment was declined by PayPal. Maps to 402.</summary>
public class PaymentDeclinedException : Exception
{
    public PaymentDeclinedException(string message) : base(message) { }
}
