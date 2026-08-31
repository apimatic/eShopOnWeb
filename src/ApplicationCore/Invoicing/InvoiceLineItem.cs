namespace Microsoft.eShopWeb.ApplicationCore.Invoicing;

/// <summary>A single billed line, snapshotted from an order item.</summary>
public record InvoiceLineItem(string ProductName, int Quantity, decimal UnitPrice, decimal TotalAmount);
