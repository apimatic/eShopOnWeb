namespace Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

public static class DefaultAddress
{
    /// <summary>Used when an API caller places an order without supplying a shipping address.</summary>
    public static readonly Address Placeholder = new Address("123 Main St", "Seattle", "WA", "United States", "98101");
}
