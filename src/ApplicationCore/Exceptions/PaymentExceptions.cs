using System;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

/// <summary>An order does not exist or does not belong to the caller (never reveals which).</summary>
public class OrderNotFoundException : Exception
{
    public OrderNotFoundException(string message) : base(message) { }
}

/// <summary>The order is in a lifecycle state that does not allow the requested action.</summary>
public class OrderStateException : Exception
{
    public OrderStateException(string message) : base(message) { }

    public OrderStateException(string message, Exception inner) : base(message, inner) { }
}

/// <summary>Caller input failed application validation before any provider call.</summary>
public class ValidationFailureException : Exception
{
    public ValidationFailureException(string message) : base(message) { }
}

/// <summary>A saved payment method does not exist or does not belong to the caller (never reveals which).</summary>
public class PaymentMethodNotFoundException : Exception
{
    public PaymentMethodNotFoundException(string message) : base(message) { }
}
