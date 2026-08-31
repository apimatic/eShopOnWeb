using System;
using Microsoft.eShopWeb.ApplicationCore.Invoicing;

namespace Microsoft.eShopWeb.PublicApi.InvoiceEndpoints;

/// <summary>Body for raising a bill: the calendar date it falls due, plus optional customer details.</summary>
public class RaiseInvoiceBody
{
    public DateOnly DueDate { get; set; }
    public string? CustomerName { get; set; }
    public string? CustomerEmail { get; set; }
}

/// <summary>
/// Body for correcting a bill. The amount is not correctable here (it comes from the order), so only
/// the due date and customer details may be supplied; any omitted field is left unchanged.
/// </summary>
public class CorrectInvoiceBody
{
    public DateOnly? DueDate { get; set; }
    public string? CustomerName { get; set; }
    public string? CustomerEmail { get; set; }
}

// ----- endpoint request contexts (assembled from route/query/body + the caller's token) -----

public record RaiseInvoiceRequest(int OrderId, DateOnly DueDate, CustomerDetails? Customer, string BuyerId, bool IsOperator);

public record CorrectInvoiceRequest(string InvoiceId, DateOnly? DueDate, CustomerDetails? Customer, string BuyerId, bool IsOperator);

public record InvoiceRef(string InvoiceId, string BuyerId, bool IsOperator);

public record MyInvoicesRequest(string BuyerId);

public record ReconciliationRequest(DateTimeOffset From, DateTimeOffset To);
