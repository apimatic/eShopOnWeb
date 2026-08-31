using System;
using System.Collections.Generic;

namespace Microsoft.eShopWeb.PublicApi.InvoiceEndpoints;

public class MyInvoicesResponse : BaseResponse
{
    public MyInvoicesResponse(Guid correlationId) : base(correlationId)
    {
    }

    public MyInvoicesResponse()
    {
    }

    /// <summary>The caller's bills, each carrying its own invoiceId and showing where it has got to.</summary>
    public List<InvoiceDto> Invoices { get; set; } = new();
}
