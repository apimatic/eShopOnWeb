using System;

namespace Microsoft.eShopWeb.PublicApi.InvoiceEndpoints;

public class WithdrawInvoiceResponse : BaseResponse
{
    public WithdrawInvoiceResponse(Guid correlationId) : base(correlationId)
    {
    }

    public WithdrawInvoiceResponse()
    {
    }

    public string InvoiceId { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public string State { get; set; } = string.Empty;

    /// <summary>After withdrawal the bill is no longer payable.</summary>
    public bool Payable { get; set; }
}
