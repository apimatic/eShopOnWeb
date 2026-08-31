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
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

/// <summary>
/// Operator report: PayPal's own record of transactions over [from, to] (ISO-8601
/// date-times) lined up against eShop orders. Covers the whole range (all pages).
/// Entries a side doesn't know about are flagged OnlyInGateway / OnlyInShop.
/// Note: PayPal's reporting lags live activity, so very recent payments may
/// legitimately appear as OnlyInShop.
/// </summary>
public class ReconciliationEndpoint : IEndpoint<IResult, ReconciliationRequest, IReconciliationService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/reconciliation",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS, AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (DateTimeOffset from, DateTimeOffset to, IReconciliationService reconciliationService) =>
            {
                return await HandleAsync(new ReconciliationRequest(from, to), reconciliationService);
            })
            .Produces<ReconciliationResponse>()
            .WithTags("OrderEndpoints");
    }

    public async Task<IResult> HandleAsync(ReconciliationRequest request, IReconciliationService reconciliationService)
    {
        if (request.To <= request.From)
        {
            return Results.BadRequest(new { Message = "to must be after from." });
        }

        var report = await reconciliationService.ReconcileAsync(request.From, request.To);

        var response = new ReconciliationResponse(request.CorrelationId())
        {
            From = report.From,
            To = report.To,
            MatchedCount = report.MatchedCount,
            OnlyInGatewayCount = report.OnlyInGatewayCount,
            OnlyInShopCount = report.OnlyInShopCount,
            Entries = report.Entries.Select(e => new ReconciliationEntryDto
            {
                MatchStatus = e.MatchStatus,
                GatewayTransactionId = e.GatewayTransactionId,
                GatewayReferenceId = e.GatewayReferenceId,
                GatewayEventCode = e.GatewayEventCode,
                GatewayDate = e.GatewayDate,
                GatewayAmount = e.GatewayAmount,
                GatewayFee = e.GatewayFee,
                GatewayStatus = e.GatewayStatus,
                OrderId = e.OrderId,
                PaymentId = e.PaymentId,
                ShopPaymentStatus = e.ShopPaymentStatus,
                ShopAmount = e.ShopAmount,
                Currency = e.Currency
            }).ToList()
        };
        return Results.Ok(response);
    }
}

public class ReconciliationRequest : BaseRequest
{
    public ReconciliationRequest(DateTimeOffset from, DateTimeOffset to)
    {
        From = from;
        To = to;
    }

    public DateTimeOffset From { get; }
    public DateTimeOffset To { get; }
}

public class ReconciliationResponse : BaseResponse
{
    public ReconciliationResponse(Guid correlationId) : base(correlationId)
    {
    }

    public ReconciliationResponse()
    {
    }

    public DateTimeOffset From { get; set; }
    public DateTimeOffset To { get; set; }
    public int MatchedCount { get; set; }
    public int OnlyInGatewayCount { get; set; }
    public int OnlyInShopCount { get; set; }
    public List<ReconciliationEntryDto> Entries { get; set; } = new List<ReconciliationEntryDto>();
}

public class ReconciliationEntryDto
{
    public string MatchStatus { get; set; } = string.Empty;
    public string? GatewayTransactionId { get; set; }
    public string? GatewayReferenceId { get; set; }
    public string? GatewayEventCode { get; set; }
    public DateTimeOffset? GatewayDate { get; set; }
    public decimal? GatewayAmount { get; set; }
    public decimal? GatewayFee { get; set; }
    public string? GatewayStatus { get; set; }
    public int? OrderId { get; set; }
    public int? PaymentId { get; set; }
    public string? ShopPaymentStatus { get; set; }
    public decimal? ShopAmount { get; set; }
    public string? Currency { get; set; }
}
