using System;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

/// <summary>
/// A resource the caller asked for does not exist — or exists but is not theirs. The two are
/// deliberately indistinguishable so one shopper cannot probe for another's orders or cards.
/// </summary>
public class PaymentResourceNotFoundException : Exception
{
    public PaymentResourceNotFoundException(string message) : base(message) { }
}

/// <summary>The operation is not valid for the payment's current state (e.g. fulfil before authorize).</summary>
public class PaymentConflictException : Exception
{
    public PaymentConflictException(string message) : base(message) { }
}

/// <summary>The request itself is invalid (e.g. no payment source, or a refund beyond the captured amount).</summary>
public class PaymentValidationException : Exception
{
    public PaymentValidationException(string message) : base(message) { }
}
