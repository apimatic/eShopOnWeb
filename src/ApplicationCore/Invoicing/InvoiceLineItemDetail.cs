namespace Microsoft.eShopWeb.ApplicationCore.Invoicing;

/// <summary>A single line the provider itemises on a bill. The provider requires a SKU per line.</summary>
public record InvoiceLineItemDetail(string Sku, string ProductName, int Quantity, decimal UnitPrice);
