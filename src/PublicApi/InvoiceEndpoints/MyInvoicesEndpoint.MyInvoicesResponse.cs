using System;
using System.Collections.Generic;

namespace Microsoft.eShopWeb.PublicApi.InvoiceEndpoints;

public class MyInvoicesResponse : BaseResponse
{
    public MyInvoicesResponse(Guid correlationId) : base(correlationId) { }

    public MyInvoicesResponse() { }

    public List<InvoiceSummaryDto> Invoices { get; set; } = new();
}
