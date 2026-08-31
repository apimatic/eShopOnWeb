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

    public List<MyInvoiceDto> Invoices { get; set; } = new();
}
