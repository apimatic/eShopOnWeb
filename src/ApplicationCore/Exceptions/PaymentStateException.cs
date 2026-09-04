using System;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

/// <summary>An operation was attempted that is not valid for the order/payment's current state.</summary>
public class PaymentStateException : Exception
{
    public PaymentStateException(string message) : base(message) { }
}