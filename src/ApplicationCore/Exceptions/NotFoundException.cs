using System;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

public abstract class NotFoundException : Exception
{
    protected NotFoundException(string message) : base(message)
    {
    }
}

public class OrderNotFoundException : NotFoundException
{
    public OrderNotFoundException(int orderId) : base($"Order {orderId} was not found.")
    {
    }
}

public class PaymentMethodNotFoundException : NotFoundException
{
    public PaymentMethodNotFoundException(int paymentMethodId) : base($"Payment method {paymentMethodId} was not found.")
    {
    }
}

public class CatalogItemNotFoundException : NotFoundException
{
    public CatalogItemNotFoundException(int catalogItemId) : base($"Catalog item {catalogItemId} was not found.")
    {
    }
}
