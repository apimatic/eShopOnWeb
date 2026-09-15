namespace Microsoft.eShopWeb.Infrastructure.PayPal.Contracts;

// --- Payments v2: authorizations, captures, refunds ---

/// <summary>An authorization as returned by Orders v2 (in purchase_units[].payments) and Payments v2.</summary>
internal sealed class AuthorizationDto
{
    public string? Id { get; set; }
    public string? Status { get; set; }
    public MoneyDto? Amount { get; set; }
    public string? ExpirationTime { get; set; }
}

/// <summary>Capture an authorization (fulfilment).</summary>
internal sealed class CaptureRequestDto
{
    public MoneyDto? Amount { get; set; }
    public string? InvoiceId { get; set; }
    public bool FinalCapture { get; set; }
    public string? NoteToPayer { get; set; }
}

internal sealed class CaptureDto
{
    public string? Id { get; set; }
    public string? Status { get; set; }
    public MoneyDto? Amount { get; set; }
    public SellerReceivableBreakdownDto? SellerReceivableBreakdown { get; set; }
}

internal sealed class SellerReceivableBreakdownDto
{
    public MoneyDto? GrossAmount { get; set; }
    public MoneyDto? PaypalFee { get; set; }
    public MoneyDto? NetAmount { get; set; }
}

/// <summary>Renew a stale authorization.</summary>
internal sealed class ReauthorizeRequestDto
{
    public MoneyDto? Amount { get; set; }
}

/// <summary>Refund a capture (full or partial).</summary>
internal sealed class RefundRequestDto
{
    public MoneyDto? Amount { get; set; }
    public string? InvoiceId { get; set; }
    public string? CustomId { get; set; }
    public string? NoteToPayer { get; set; }
}

internal sealed class RefundDto
{
    public string? Id { get; set; }
    public string? Status { get; set; }
    public MoneyDto? Amount { get; set; }
    public SellerPayableBreakdownDto? SellerPayableBreakdown { get; set; }
}

internal sealed class SellerPayableBreakdownDto
{
    public MoneyDto? GrossAmount { get; set; }
    public MoneyDto? PaypalFee { get; set; }
    public MoneyDto? NetAmount { get; set; }
    public MoneyDto? TotalRefundedAmount { get; set; }
}
