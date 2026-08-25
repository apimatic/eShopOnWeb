using Microsoft.eShopWeb.ApplicationCore.PaymentProcessing;

namespace Microsoft.eShopWeb.PublicApi.ReconciliationEndpoints;

public class ReconciliationEntryDto
{
    public string? PayPalTransactionId { get; set; }
    public int? OrderId { get; set; }
    public decimal? PayPalAmount { get; set; }
    public decimal? EShopAmount { get; set; }
    public string? Currency { get; set; }
    public string? PayPalStatus { get; set; }
    public string Note { get; set; } = string.Empty;

    public static ReconciliationEntryDto From(ReconciliationEntry entry) => new()
    {
        PayPalTransactionId = entry.PayPalTransactionId,
        OrderId = entry.OrderId,
        PayPalAmount = entry.PayPalAmount,
        EShopAmount = entry.EShopAmount,
        Currency = entry.Currency,
        PayPalStatus = entry.PayPalStatus,
        Note = entry.Note
    };
}
