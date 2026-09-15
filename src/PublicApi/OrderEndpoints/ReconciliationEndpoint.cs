using System;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

public class ReconciliationEndpoint : IEndpoint<IResult, ReconciliationQuery, IOrderPaymentService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/reconciliation",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS, AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
            async (DateTimeOffset from, DateTimeOffset to, IOrderPaymentService orders) =>
                await HandleAsync(new ReconciliationQuery(from, to), orders))
            .Produces<ReconciliationResponse>()
            .WithTags("OrderEndpoints");
    }

    public async Task<IResult> HandleAsync(ReconciliationQuery query, IOrderPaymentService orders)
    {
        var report = await orders.ReconcileAsync(query.From, query.To);
        return Results.Ok(new ReconciliationResponse
        {
            From = report.From,
            To = report.To,
            PaypalTransactionCount = report.PaypalTransactions.Count,
            Matches = report.Matches.Select(m => new ReconciliationMatchResponse
            {
                PaypalTransactionId = m.Paypal.TransactionId,
                PaypalStatus = m.Paypal.Status,
                PaypalAmount = m.Paypal.Amount,
                PaypalInvoiceId = m.Paypal.InvoiceId,
                OrderId = m.Eshop.OrderId,
                EshopKind = m.Eshop.Kind,
                EshopPaypalId = m.Eshop.PaypalId
            }).ToList(),
            PaypalOnly = report.PaypalOnly.Select(t => new PaypalOnlyResponse
            {
                TransactionId = t.TransactionId,
                Status = t.Status,
                Amount = t.Amount,
                Currency = t.Currency,
                InvoiceId = t.InvoiceId,
                CustomField = t.CustomField,
                EventCode = t.EventCode,
                InitiationDate = t.InitiationDate
            }).ToList(),
            EshopOnly = report.EshopOnly.Select(e => new EshopOnlyResponse
            {
                OrderId = e.OrderId,
                Kind = e.Kind,
                PaypalId = e.PaypalId,
                Status = e.Status,
                Amount = e.Amount,
                OccurredAt = e.OccurredAt
            }).ToList()
        });
    }
}

public class ReconciliationQuery
{
    public ReconciliationQuery(DateTimeOffset from, DateTimeOffset to)
    {
        From = from;
        To = to;
    }

    public DateTimeOffset From { get; }
    public DateTimeOffset To { get; }
}

public class ReconciliationResponse
{
    public DateTimeOffset From { get; set; }
    public DateTimeOffset To { get; set; }
    public int PaypalTransactionCount { get; set; }
    public System.Collections.Generic.List<ReconciliationMatchResponse> Matches { get; set; } = new();
    public System.Collections.Generic.List<PaypalOnlyResponse> PaypalOnly { get; set; } = new();
    public System.Collections.Generic.List<EshopOnlyResponse> EshopOnly { get; set; } = new();
}

public class ReconciliationMatchResponse
{
    public string PaypalTransactionId { get; set; } = string.Empty;
    public string? PaypalStatus { get; set; }
    public decimal? PaypalAmount { get; set; }
    public string? PaypalInvoiceId { get; set; }
    public int OrderId { get; set; }
    public string EshopKind { get; set; } = string.Empty;
    public string EshopPaypalId { get; set; } = string.Empty;
}

public class PaypalOnlyResponse
{
    public string TransactionId { get; set; } = string.Empty;
    public string? Status { get; set; }
    public decimal? Amount { get; set; }
    public string? Currency { get; set; }
    public string? InvoiceId { get; set; }
    public string? CustomField { get; set; }
    public string? EventCode { get; set; }
    public DateTimeOffset? InitiationDate { get; set; }
}

public class EshopOnlyResponse
{
    public int OrderId { get; set; }
    public string Kind { get; set; } = string.Empty;
    public string PaypalId { get; set; } = string.Empty;
    public string? Status { get; set; }
    public decimal? Amount { get; set; }
    public DateTimeOffset? OccurredAt { get; set; }
}
