using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.ReconciliationEndpoints;

public class ReconciliationRequest : BaseRequest
{
    internal DateTimeOffset From { get; set; }
    internal DateTimeOffset To { get; set; }
}

public class ReconciliationTransactionDto
{
    public string TransactionId { get; set; } = string.Empty;
    public string? ReferenceId { get; set; }
    public string EventCode { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public decimal Amount { get; set; }
    public decimal Fee { get; set; }
    public string Currency { get; set; } = string.Empty;
    public string Date { get; set; } = string.Empty;
    public string? InvoiceId { get; set; }
    public int? OrderId { get; set; }
    public string? EShopPaymentStatus { get; set; }
}

public class ReconciliationEShopEntryDto
{
    public int OrderId { get; set; }
    public string Kind { get; set; } = string.Empty;
    public string PayPalId { get; set; } = string.Empty;
    public decimal Amount { get; set; }
    public string EShopPaymentStatus { get; set; } = string.Empty;
}

public class ReconciliationResponse : BaseResponse
{
    public ReconciliationResponse(Guid correlationId) : base(correlationId) { }

    public string From { get; set; } = string.Empty;
    public string To { get; set; } = string.Empty;
    public string Currency { get; set; } = string.Empty;
    public int PayPalTransactionCount { get; set; }
    public int MatchedCount { get; set; }

    /// <summary>PayPal transactions that line up with an eShop order.</summary>
    public List<ReconciliationTransactionDto> Matched { get; set; } = new();

    /// <summary>Transactions PayPal knows about that eShop does not.</summary>
    public List<ReconciliationTransactionDto> InPayPalNotInEShop { get; set; } = new();

    /// <summary>Settlements eShop knows about that are not (yet) in PayPal's record for the range.</summary>
    public List<ReconciliationEShopEntryDto> InEShopNotInPayPal { get; set; } = new();
}

/// <summary>
/// Operator action: lists PayPal's own transactions for a date range and lines them up against
/// eShop orders, so a discrepancy on either side is visible. Covers the whole range, not just the
/// first page. Restricted to administrators.
/// </summary>
public class ReconciliationEndpoint : IEndpoint<IResult, ReconciliationRequest, IReconciliationService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/reconciliation",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS, AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (DateTimeOffset? from, DateTimeOffset? to, IReconciliationService service) =>
            {
                if (from is null || to is null)
                {
                    throw new PaymentValidationException("Both 'from' and 'to' ISO-8601 date-times are required.");
                }
                return await HandleAsync(new ReconciliationRequest { From = from.Value, To = to.Value }, service);
            })
            .Produces<ReconciliationResponse>()
            .WithTags("ReconciliationEndpoints");
    }

    public async Task<IResult> HandleAsync(ReconciliationRequest request, IReconciliationService service)
    {
        var report = await service.ReconcileAsync(request.From, request.To);

        var response = new ReconciliationResponse(request.CorrelationId())
        {
            From = report.From.ToString("o"),
            To = report.To.ToString("o"),
            Currency = report.CurrencyCode,
            PayPalTransactionCount = report.PayPalTransactionCount,
            MatchedCount = report.Matched.Count,
            Matched = report.Matched.Select(m => new ReconciliationTransactionDto
            {
                TransactionId = m.PayPalTransactionId,
                EventCode = m.EventCode,
                Amount = m.Amount,
                Currency = m.CurrencyCode,
                Date = m.Date.ToString("o"),
                OrderId = m.OrderId,
                EShopPaymentStatus = m.EShopPaymentStatus
            }).ToList(),
            InPayPalNotInEShop = report.InPayPalNotInEShop.Select(t => new ReconciliationTransactionDto
            {
                TransactionId = t.TransactionId,
                ReferenceId = t.ReferenceId,
                EventCode = t.EventCode,
                Status = t.Status,
                Amount = t.Amount,
                Fee = t.Fee,
                Currency = t.CurrencyCode,
                Date = t.Date.ToString("o"),
                InvoiceId = t.InvoiceId
            }).ToList(),
            InEShopNotInPayPal = report.InEShopNotInPayPal.Select(e => new ReconciliationEShopEntryDto
            {
                OrderId = e.OrderId,
                Kind = e.Kind,
                PayPalId = e.PayPalId,
                Amount = e.Amount,
                EShopPaymentStatus = e.EShopPaymentStatus
            }).ToList()
        };
        return Results.Ok(response);
    }
}
