using System;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

public class OrderNotFoundException : Exception
{
    public OrderNotFoundException(int orderId)
        : base($"Order {orderId} was not found.") {}
}

public class SavedPaymentMethodNotFoundException : Exception
{
    public SavedPaymentMethodNotFoundException(int paymentMethodId)
        : base($"Saved payment method {paymentMethodId} was not found.") {}
}
