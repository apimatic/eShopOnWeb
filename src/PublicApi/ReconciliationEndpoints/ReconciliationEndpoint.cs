using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.PayPal;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.ReconciliationEndpoints;

public class ReconciliationResponse : BaseResponse
{
    public ReconciliationResponse(Guid correlationId) : base(correlationId) { }
    public ReconciliationResponse() { }

    public DateTimeOffset From { get; set; }
    public DateTimeOffset To { get; set; }
    public int PayPalTransactionCount { get; set; }
    public int EShopOrderCount { get; set; }

    /// <summary>eShop orders lined up against the PayPal transaction(s) that belong to them.</summary>
    public List<MatchDto> Matched { get; set; } = new();

    /// <summary>Transactions PayPal knows about but eShop has no order for.</summary>
    public List<TransactionDto> InPayPalNotInEShop { get; set; } = new();

    /// <summary>eShop orders PayPal has no transaction for in this range.</summary>
    public List<OrderSummaryDto> InEShopNotInPayPal { get; set; } = new();

    public class MatchDto
    {
        public OrderSummaryDto Order { get; set; } = new();
        public List<TransactionDto> Transactions { get; set; } = new();
    }

    public class OrderSummaryDto
    {
        public int OrderId { get; set; }
        public string? Reference { get; set; }
        public string BuyerId { get; set; } = string.Empty;
        public decimal OrderTotal { get; set; }
        public string PaymentStatus { get; set; } = string.Empty;
        public string? PayPalOrderId { get; set; }
        public string? CaptureId { get; set; }
        public DateTimeOffset OrderDate { get; set; }

        public static OrderSummaryDto From(ReconciliationOrder o) => new()
        {
            OrderId = o.OrderId,
            Reference = o.Reference,
            BuyerId = o.BuyerId,
            OrderTotal = o.OrderTotal,
            PaymentStatus = o.PaymentStatus,
            PayPalOrderId = o.PayPalOrderId,
            CaptureId = o.CaptureId,
            OrderDate = o.OrderDate
        };
    }

    public class TransactionDto
    {
        public string TransactionId { get; set; } = string.Empty;
        public string? ReferenceId { get; set; }
        public string? InvoiceId { get; set; }
        public string? Status { get; set; }
        public string? EventCode { get; set; }
        public decimal Amount { get; set; }
        public decimal Fee { get; set; }
        public string? Currency { get; set; }
        public DateTimeOffset? InitiationDate { get; set; }

        public static TransactionDto From(PayPalTransaction t) => new()
        {
            TransactionId = t.TransactionId,
            ReferenceId = t.ReferenceId,
            InvoiceId = t.InvoiceId,
            Status = t.Status,
            EventCode = t.EventCode,
            Amount = t.Amount,
            Fee = t.Fee,
            Currency = t.Currency,
            InitiationDate = t.InitiationDate
        };
    }
}

/// <summary>
/// GET /api/reconciliation?from={from}&amp;to={to} — operator report lining PayPal's own record of
/// transactions over a date range up against eShop orders. Covers the whole range (paginated and
/// chunked). Restricted to the administrator role.
/// </summary>
public class ReconciliationEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/reconciliation",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS,
                       AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (DateTimeOffset from, DateTimeOffset to, IReconciliationService service) =>
                await HandleAsync(from, to, service))
            .Produces<ReconciliationResponse>()
            .WithTags("ReconciliationEndpoints");
    }

    private static async Task<IResult> HandleAsync(DateTimeOffset from, DateTimeOffset to, IReconciliationService service)
    {
        if (to < from)
            return Results.Json(new { statusCode = 400, message = "'to' must be on or after 'from'." }, statusCode: StatusCodes.Status400BadRequest);

        var report = await service.BuildAsync(from, to);

        var response = new ReconciliationResponse
        {
            From = report.From,
            To = report.To,
            PayPalTransactionCount = report.PayPalTransactionCount,
            EShopOrderCount = report.EShopOrderCount,
            Matched = report.Matched.Select(m => new ReconciliationResponse.MatchDto
            {
                Order = ReconciliationResponse.OrderSummaryDto.From(m.Order),
                Transactions = m.Transactions.Select(ReconciliationResponse.TransactionDto.From).ToList()
            }).ToList(),
            InPayPalNotInEShop = report.InPayPalNotInEShop.Select(ReconciliationResponse.TransactionDto.From).ToList(),
            InEShopNotInPayPal = report.InEShopNotInPayPal.Select(ReconciliationResponse.OrderSummaryDto.From).ToList()
        };
        return Results.Ok(response);
    }
}
