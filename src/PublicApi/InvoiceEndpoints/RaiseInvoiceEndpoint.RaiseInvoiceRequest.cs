using System;

namespace Microsoft.eShopWeb.PublicApi.InvoiceEndpoints;

public class RaiseInvoiceRequest
{
    /// <summary>The calendar date the bill falls due (ISO-8601 date, e.g. 2026-09-30).</summary>
    public DateOnly DueDate { get; set; }
}
