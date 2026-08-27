using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.ReconciliationEndpoints;

/// <summary>
/// Operator action: lists PayPal's own record of transactions for a date range and lines
/// them up against eShop orders, covering the whole range rather than the first page.
/// </summary>
public class GetReconciliationEndpoint : IEndpoint<IResult, GetReconciliationRequest, IReconciliationService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/reconciliation",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS, AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (DateTimeOffset from, DateTimeOffset to, IReconciliationService reconciliationService) =>
            {
                return await HandleAsync(new GetReconciliationRequest { From = from, To = to }, reconciliationService);
            })
            .Produces<GetReconciliationResponse>()
            .WithTags("ReconciliationEndpoints");
    }

    public async Task<IResult> HandleAsync(GetReconciliationRequest request, IReconciliationService reconciliationService)
    {
        var report = await reconciliationService.GetReportAsync(request.From, request.To);

        var response = new GetReconciliationResponse(request.CorrelationId())
        {
            From = report.From,
            To = report.To,
            PayPalTransactionCount = report.PayPalTransactionCount,
            Transactions = report.Transactions.Select(t => new ReconciliationTransactionDto
            {
                TransactionId = t.TransactionId,
                EventCode = t.EventCode,
                Status = t.Status,
                Amount = t.Amount,
                Currency = t.Currency,
                Fee = t.Fee,
                InvoiceId = t.InvoiceId,
                Time = t.Time,
                MatchedOrderId = t.MatchedOrderId,
                MatchType = t.MatchType
            }).ToList(),
            UnmatchedEShopOrders = report.UnmatchedEShopOrders.Select(o => new UnmatchedEShopOrderDto
            {
                OrderId = o.OrderId,
                PayPalOrderId = o.PayPalOrderId,
                AuthorizationId = o.AuthorizationId,
                CaptureId = o.CaptureId,
                RefundIds = o.RefundIds.ToList()
            }).ToList()
        };
        return Results.Ok(response);
    }
}

public class GetReconciliationRequest : BaseRequest
{
    [Required]
    public DateTimeOffset From { get; set; }

    [Required]
    public DateTimeOffset To { get; set; }
}

public class GetReconciliationResponse : BaseResponse
{
    public GetReconciliationResponse(Guid correlationId) : base(correlationId) { }
    public GetReconciliationResponse() { }

    public DateTimeOffset From { get; set; }
    public DateTimeOffset To { get; set; }
    public int PayPalTransactionCount { get; set; }
    public List<ReconciliationTransactionDto> Transactions { get; set; } = new();
    public List<UnmatchedEShopOrderDto> UnmatchedEShopOrders { get; set; } = new();
}

public class ReconciliationTransactionDto
{
    public string TransactionId { get; set; } = string.Empty;
    public string? EventCode { get; set; }
    public string? Status { get; set; }
    public decimal? Amount { get; set; }
    public string? Currency { get; set; }
    public decimal? Fee { get; set; }
    public string? InvoiceId { get; set; }
    public DateTimeOffset? Time { get; set; }

    /// <summary>The eShop order this PayPal transaction lines up with, or null when PayPal knows of a payment eShop doesn't.</summary>
    public int? MatchedOrderId { get; set; }
    public string MatchType { get; set; } = string.Empty;
}

public class UnmatchedEShopOrderDto
{
    public int OrderId { get; set; }
    public string? PayPalOrderId { get; set; }
    public string? AuthorizationId { get; set; }
    public string? CaptureId { get; set; }
    public List<string> RefundIds { get; set; } = new();
}
