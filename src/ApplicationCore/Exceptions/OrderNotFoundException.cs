namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

public class OrderNotFoundException : ApiException
{
    public OrderNotFoundException(int orderId)
        : base($"Order {orderId} was not found.", 404)
    {
    }
}