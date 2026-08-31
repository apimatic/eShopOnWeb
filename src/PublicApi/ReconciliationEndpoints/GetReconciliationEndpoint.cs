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

namespace Microsoft.eShopWeb.PublicApi.ReconciliationEndpoints;

/// <summary>
/// Operator: PayPal's own record of transactions over a date range, lined up
/// against eShop orders. Covers the whole range, not just the first page.
/// </summary>
public class GetReconciliationEndpoint : IEndpoint<IResult, GetReconciliationRequest, IPaymentService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/reconciliation",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS, AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (DateTimeOffset from, DateTimeOffset to, IPaymentService paymentService) =>
            {
                return await HandleAsync(new GetReconciliationRequest(from, to), paymentService);
            })
            .Produces<GetReconciliationResponse>()
            .WithTags("ReconciliationEndpoints");
    }

    public async Task<IResult> HandleAsync(GetReconciliationRequest request, IPaymentService paymentService)
    {
        var response = new GetReconciliationResponse(request.CorrelationId());

        var report = await paymentService.GetReconciliationAsync(request.From, request.To);

        response.From = report.From;
        response.To = report.To;
        response.Transactions = report.Transactions.Select(t => new ReconciliationTransactionDto
        {
            TransactionId = t.TransactionId,
            Status = t.Status,
            Amount = t.Amount,
            Currency = t.Currency,
            Fee = t.Fee,
            InitiatedAt = t.InitiatedAt,
            MatchedOrderId = t.MatchedOrderId,
            MatchedAs = t.MatchedAs
        }).ToList();
        response.CapturesMissingFromPayPal = report.CapturesMissingFromPayPal.Select(c => new MissingCaptureDto
        {
            OrderId = c.OrderId,
            CaptureId = c.CaptureId,
            Amount = c.Amount,
            Currency = c.Currency,
            CapturedAt = c.CapturedAt
        }).ToList();
        return Results.Ok(response);
    }
}

public class GetReconciliationRequest : BaseRequest
{
    public DateTimeOffset From { get; init; }
    public DateTimeOffset To { get; init; }

    public GetReconciliationRequest(DateTimeOffset from, DateTimeOffset to)
    {
        From = from;
        To = to;
    }
}

public class GetReconciliationResponse : BaseResponse
{
    public GetReconciliationResponse(Guid correlationId) : base(correlationId) { }

    public DateTimeOffset From { get; set; }
    public DateTimeOffset To { get; set; }
    public List<ReconciliationTransactionDto> Transactions { get; set; } = new List<ReconciliationTransactionDto>();
    public List<MissingCaptureDto> CapturesMissingFromPayPal { get; set; } = new List<MissingCaptureDto>();
}

public class ReconciliationTransactionDto
{
    public string TransactionId { get; set; } = string.Empty;
    public string? Status { get; set; }
    public decimal? Amount { get; set; }
    public string? Currency { get; set; }
    public decimal? Fee { get; set; }
    public DateTimeOffset? InitiatedAt { get; set; }

    /// <summary>The eShop order this PayPal transaction belongs to, or null when PayPal knows it and eShop does not.</summary>
    public int? MatchedOrderId { get; set; }

    /// <summary>How the match was made: authorization, capture or refund.</summary>
    public string? MatchedAs { get; set; }
}

public class MissingCaptureDto
{
    public int OrderId { get; set; }
    public string CaptureId { get; set; } = string.Empty;
    public decimal Amount { get; set; }
    public string Currency { get; set; } = string.Empty;
    public DateTimeOffset CapturedAt { get; set; }
}
