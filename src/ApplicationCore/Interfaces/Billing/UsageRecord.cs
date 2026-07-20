namespace Microsoft.eShopWeb.ApplicationCore.Interfaces.Billing;

public class UsageRecord
{
    public UsageRecord(long id, decimal quantity, string? memo)
    {
        Id = id;
        Quantity = quantity;
        Memo = memo;
    }

    public long Id { get; }
    public decimal Quantity { get; }
    public string? Memo { get; }
}
