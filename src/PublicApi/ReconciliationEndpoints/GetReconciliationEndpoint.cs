using System;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.PublicApi.OrderEndpoints;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.ReconciliationEndpoints;

public class GetReconciliationEndpoint : IEndpoint<IResult, IPaidOrderService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/reconciliation",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS,
                AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
            async (string from, string to, IPaidOrderService service) => await HandleAsync(from, to, service))
            .Produces<ReconciliationResponse>()
            .WithTags("ReconciliationEndpoints");
    }

    public Task<IResult> HandleAsync(IPaidOrderService service) =>
        Task.FromResult(Results.BadRequest());

    private static async Task<IResult> HandleAsync(string from, string to, IPaidOrderService service)
    {
        if (!DateTimeOffset.TryParse(from, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var fromDate) ||
            !DateTimeOffset.TryParse(to, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var toDate))
        {
            throw new OrderPaymentException(400, "Query parameters 'from' and 'to' must be ISO-8601 date-times.");
        }

        var report = await service.ReconcileAsync(fromDate, toDate);
        return Results.Ok(new ReconciliationResponse
        {
            From = report.From,
            To = report.To,
            Matched = report.Matched.Select(m => new ReconciliationMatchDto
            {
                OrderId = m.Order.Id,
                EshopStatus = m.Order.Status.ToString(),
                PayPalTransactionId = m.Transaction.TransactionId,
                PayPalReferenceId = m.Transaction.ReferenceId,
                PayPalInvoiceId = m.Transaction.InvoiceId,
                PayPalStatus = m.Transaction.Status,
                PayPalEventCode = m.Transaction.EventCode,
                Amount = m.Transaction.Amount,
                Currency = m.Transaction.Currency,
                InitiationDate = m.Transaction.InitiationDate
            }).ToList(),
            PayPalOnly = report.PayPalOnly.Select(t => new PayPalOnlyTransactionDto
            {
                PayPalTransactionId = t.TransactionId,
                PayPalReferenceId = t.ReferenceId,
                PayPalInvoiceId = t.InvoiceId,
                CustomField = t.CustomField,
                Status = t.Status,
                EventCode = t.EventCode,
                Amount = t.Amount,
                Currency = t.Currency,
                InitiationDate = t.InitiationDate,
                Fee = t.Fee
            }).ToList(),
            EshopOnly = report.EshopOnly.Select(o => new EshopOnlyOrderDto
            {
                OrderId = o.Id,
                Status = o.Status.ToString(),
                PayPalOrderId = o.PayPalOrderId,
                AuthorizationId = o.PayPalAuthorizationId,
                CaptureId = o.PayPalCaptureId,
                Total = o.Total(),
                Currency = o.Currency
            }).ToList()
        });
    }
}

public class ReconciliationResponse
{
    public DateTimeOffset From { get; set; }
    public DateTimeOffset To { get; set; }
    public System.Collections.Generic.List<ReconciliationMatchDto> Matched { get; set; } = new();
    public System.Collections.Generic.List<PayPalOnlyTransactionDto> PayPalOnly { get; set; } = new();
    public System.Collections.Generic.List<EshopOnlyOrderDto> EshopOnly { get; set; } = new();
}

public class ReconciliationMatchDto
{
    public int OrderId { get; set; }
    public string EshopStatus { get; set; } = string.Empty;
    public string PayPalTransactionId { get; set; } = string.Empty;
    public string? PayPalReferenceId { get; set; }
    public string? PayPalInvoiceId { get; set; }
    public string? PayPalStatus { get; set; }
    public string? PayPalEventCode { get; set; }
    public decimal? Amount { get; set; }
    public string? Currency { get; set; }
    public DateTimeOffset? InitiationDate { get; set; }
}

public class PayPalOnlyTransactionDto
{
    public string PayPalTransactionId { get; set; } = string.Empty;
    public string? PayPalReferenceId { get; set; }
    public string? PayPalInvoiceId { get; set; }
    public string? CustomField { get; set; }
    public string? Status { get; set; }
    public string? EventCode { get; set; }
    public decimal? Amount { get; set; }
    public string? Currency { get; set; }
    public DateTimeOffset? InitiationDate { get; set; }
    public decimal? Fee { get; set; }
}

public class EshopOnlyOrderDto
{
    public int OrderId { get; set; }
    public string Status { get; set; } = string.Empty;
    public string? PayPalOrderId { get; set; }
    public string? AuthorizationId { get; set; }
    public string? CaptureId { get; set; }
    public decimal Total { get; set; }
    public string? Currency { get; set; }
}
