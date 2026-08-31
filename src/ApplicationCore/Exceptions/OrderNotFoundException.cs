namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

public class OrderNotFoundException : ResourceNotFoundException
{
    public OrderNotFoundException(int orderId)
        : base($"No order found with id {orderId}")
    {
    }
}
