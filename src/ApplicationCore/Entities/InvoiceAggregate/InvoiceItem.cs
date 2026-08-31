using Ardalis.GuardClauses;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.InvoiceAggregate;

/// <summary>
/// A snapshot of a single billed line, taken from the order at the moment the
/// bill was raised. Like <see cref="OrderAggregate.OrderItem"/> it is captured so
/// that what was billed does not change if the catalog or order later does.
/// </summary>
public class InvoiceItem : BaseEntity
{
#pragma warning disable CS8618 // Required by Entity Framework
    private InvoiceItem() { }
#pragma warning restore CS8618

    public InvoiceItem(string productSku, string productName, decimal unitPrice, int units)
    {
        Guard.Against.NullOrEmpty(productSku, nameof(productSku));
        Guard.Against.NullOrEmpty(productName, nameof(productName));

        ProductSku = productSku;
        ProductName = productName;
        UnitPrice = unitPrice;
        Units = units;
    }

    /// <summary>Stock keeping unit; derived from the catalog item id.</summary>
    public string ProductSku { get; private set; }
    public string ProductName { get; private set; }
    public decimal UnitPrice { get; private set; }
    public int Units { get; private set; }
}
