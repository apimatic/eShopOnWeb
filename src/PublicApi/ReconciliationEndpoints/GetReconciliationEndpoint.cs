using System;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using BlazorShared.Authorization;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.ReconciliationEndpoints;

public class GetReconciliationEndpoint : IEndpoint<IResult, ReconciliationQuery, IReconciliationService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/reconciliation",
            [Authorize(Roles = Constants.Roles.ADMINISTRATORS, AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
            async (string from, string to, IReconciliationService service) =>
            {
                return await HandleAsync(new ReconciliationQuery { From = from, To = to }, service);
            })
            .Produces<ReconciliationResponse>()
            .WithTags("ReconciliationEndpoints");
    }

    public async Task<IResult> HandleAsync(ReconciliationQuery request, IReconciliationService service)
    {
        if (!DateTimeOffset.TryParse(request.From, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var from) ||
            !DateTimeOffset.TryParse(request.To, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var to))
        {
            throw new CheckoutException(400, "`from` and `to` must be ISO-8601 date-times.", "INVALID_DATE_RANGE");
        }

        var report = await service.ReconcileAsync(from, to);
        return Results.Ok(new ReconciliationResponse
        {
            From = report.From,
            To = report.To,
            Matched = report.Matched.Select(m => new ReconciliationMatchResponse
            {
                OrderId = m.Order.OrderId,
                OrderStatus = m.Order.Status,
                PayPalTransactionId = m.Transaction.TransactionId,
                PayPalReferenceId = m.Transaction.PaypalReferenceId,
                InvoiceId = m.Transaction.InvoiceId,
                Amount = m.Transaction.Amount,
                Currency = m.Transaction.Currency
            }).ToList(),
            PaypalOnly = report.PaypalOnly.Select(t => new PaypalTransactionResponse
            {
                TransactionId = t.TransactionId,
                PaypalReferenceId = t.PaypalReferenceId,
                InvoiceId = t.InvoiceId,
                CustomField = t.CustomField,
                EventCode = t.EventCode,
                Status = t.Status,
                Amount = t.Amount,
                Currency = t.Currency,
                InitiationDate = t.InitiationDate
            }).ToList(),
            EshopOnly = report.EshopOnly.Select(o => new EshopUnmatchedResponse
            {
                OrderId = o.OrderId,
                Status = o.Status,
                PayPalOrderId = o.PayPalOrderId,
                AuthorizationId = o.AuthorizationId,
                CaptureId = o.CaptureId
            }).ToList()
        });
    }
}

public class ReconciliationQuery
{
    public string From { get; set; } = string.Empty;
    public string To { get; set; } = string.Empty;
}

public class ReconciliationResponse
{
    public DateTimeOffset From { get; set; }
    public DateTimeOffset To { get; set; }
    public System.Collections.Generic.List<ReconciliationMatchResponse> Matched { get; set; } = new();
    public System.Collections.Generic.List<PaypalTransactionResponse> PaypalOnly { get; set; } = new();
    public System.Collections.Generic.List<EshopUnmatchedResponse> EshopOnly { get; set; } = new();
}

public class ReconciliationMatchResponse
{
    public int OrderId { get; set; }
    public string OrderStatus { get; set; } = string.Empty;
    public string PayPalTransactionId { get; set; } = string.Empty;
    public string? PayPalReferenceId { get; set; }
    public string? InvoiceId { get; set; }
    public decimal? Amount { get; set; }
    public string? Currency { get; set; }
}

public class PaypalTransactionResponse
{
    public string TransactionId { get; set; } = string.Empty;
    public string? PaypalReferenceId { get; set; }
    public string? InvoiceId { get; set; }
    public string? CustomField { get; set; }
    public string? EventCode { get; set; }
    public string? Status { get; set; }
    public decimal? Amount { get; set; }
    public string? Currency { get; set; }
    public DateTimeOffset? InitiationDate { get; set; }
}

public class EshopUnmatchedResponse
{
    public int OrderId { get; set; }
    public string Status { get; set; } = string.Empty;
    public string? PayPalOrderId { get; set; }
    public string? AuthorizationId { get; set; }
    public string? CaptureId { get; set; }
}
