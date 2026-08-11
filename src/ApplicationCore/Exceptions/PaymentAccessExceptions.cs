using System;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

/// <summary>The order does not exist, or does not belong to the caller (kept indistinguishable on purpose).</summary>
public class OrderNotFoundException : Exception
{
    public OrderNotFoundException(int orderId) : base($"Order {orderId} was not found.") { }
}

/// <summary>The saved card does not exist, or does not belong to the caller.</summary>
public class SavedCardNotFoundException : Exception
{
    public SavedCardNotFoundException(int savedCardId) : base($"Saved card {savedCardId} was not found.") { }
}

/// <summary>The request into a payment operation was malformed (e.g. neither/both of card and saved card).</summary>
public class PaymentValidationException : Exception
{
    public PaymentValidationException(string message) : base(message) { }
}
