using System;

namespace Microsoft.eShopWeb.ApplicationCore.Payments;

/// <summary>The requested order/payment does not exist for this caller (also used for cross-owner access).</summary>
public class PaymentNotFoundException : Exception
{
    public PaymentNotFoundException(string message) : base(message) { }
}

/// <summary>The requested action is not valid for the payment's current state (e.g. cancel after capture).</summary>
public class PaymentStateException : Exception
{
    public PaymentStateException(string message) : base(message) { }
}
