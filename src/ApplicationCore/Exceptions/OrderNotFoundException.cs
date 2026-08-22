namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

public class OrderNotFoundException : System.Exception
{
    public OrderNotFoundException(int orderId)
        : base($"Order {orderId} was not found.")
    {
    }
}
