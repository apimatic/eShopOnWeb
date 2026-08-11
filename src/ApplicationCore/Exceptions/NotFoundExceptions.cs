using System;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

/// <summary>The requested order does not exist, or does not belong to the caller. Surfaced as 404.</summary>
public class OrderNotFoundException : Exception
{
    public OrderNotFoundException(int orderId)
        : base($"No order with id {orderId} was found for this account.") { }
}

/// <summary>The requested saved card does not exist, or does not belong to the caller. Surfaced as 404.</summary>
public class PaymentMethodNotFoundException : Exception
{
    public PaymentMethodNotFoundException(int paymentMethodId)
        : base($"No saved payment method with id {paymentMethodId} was found for this account.") { }
}
