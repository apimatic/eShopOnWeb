namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

/// <summary>
/// Raised when an order (or its payment) cannot be found for the acting shopper. The same
/// exception is used whether the order does not exist or belongs to another shopper, so
/// ownership is never leaked.
/// </summary>
public class OrderNotFoundException : PaymentException
{
    public OrderNotFoundException(int orderId)
        : base($"Order {orderId} was not found.")
    {
    }
}
