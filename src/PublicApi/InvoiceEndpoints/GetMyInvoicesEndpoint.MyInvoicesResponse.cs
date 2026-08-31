using System;
using System.Collections.Generic;

namespace Microsoft.eShopWeb.PublicApi.InvoiceEndpoints;

public class MyInvoicesRequest : BaseRequest
{
}

public class MyInvoicesResponse : BaseResponse
{
    public MyInvoicesResponse(Guid correlationId) : base(correlationId)
    {
    }

    public MyInvoicesResponse()
    {
    }

    public List<MyInvoiceItem> Invoices { get; set; } = new();
}

public class MyInvoiceItem
{
    /// <summary>The provider's invoice identifier — what the operator endpoints act on.</summary>
    public string InvoiceId { get; set; } = string.Empty;

    public int OrderId { get; set; }
    public string LocalStatus { get; set; } = string.Empty;
    public DateTimeOffset DueDate { get; set; }
    public decimal Amount { get; set; }
    public string Currency { get; set; } = string.Empty;
}
