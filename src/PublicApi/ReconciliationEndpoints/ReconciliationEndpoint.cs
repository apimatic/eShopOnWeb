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
using Microsoft.eShopWeb.ApplicationCore.PaymentProcessing;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.ReconciliationEndpoints;

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

public class PayPalTransactionDto
{
    public string? TransactionId { get; set; }
    public decimal? Amount { get; set; }
    public string? Currency { get; set; }
    public string? Status { get; set; }
    public DateTimeOffset? Date { get; set; }
}

public class ReconciledOrderDto
{
    public int OrderId { get; set; }
    public string? CaptureId { get; set; }
    public decimal? CapturedAmount { get; set; }
    public string? CaptureStatus { get; set; }
    public DateTimeOffset? CapturedAt { get; set; }
}

public class ReconciliationResponse : BaseResponse
{
    public ReconciliationResponse(Guid correlationId) : base(correlationId) { }

    public DateTimeOffset From { get; set; }
    public DateTimeOffset To { get; set; }
    public List<PayPalTransactionDto> PayPalTransactions { get; set; } = new();
    public List<ReconciledOrderDto> MatchedOrders { get; set; } = new();

    // A PayPal transaction in range with no matching eShop order.
    public List<PayPalTransactionDto> UnmatchedPayPalTransactions { get; set; } = new();

    // An eShop order captured in range that PayPal's transaction search did not return.
    public List<ReconciledOrderDto> UnmatchedLocalOrders { get; set; } = new();
}

/// <summary>
/// Operator report: PayPal's own transactions for a date range lined up against eShop orders.
/// </summary>
public class ReconciliationEndpoint : IEndpoint<IResult, ReconciliationRequest, IOrderPaymentService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/reconciliation",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS, AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (DateTimeOffset from, DateTimeOffset to, IOrderPaymentService paymentService) =>
            {
                return await HandleAsync(new ReconciliationRequest(from, to), paymentService);
            })
            .Produces<ReconciliationResponse>()
            .WithTags("ReconciliationEndpoints");
    }

    public async Task<IResult> HandleAsync(ReconciliationRequest request, IOrderPaymentService paymentService)
    {
        if (request.To < request.From)
        {
            return Results.BadRequest("'to' must not be earlier than 'from'.");
        }

        var report = await paymentService.ReconcileAsync(request.From, request.To);

        var response = new ReconciliationResponse(request.CorrelationId())
        {
            From = report.From,
            To = report.To,
            PayPalTransactions = report.PayPalTransactions.Select(ToDto).ToList(),
            MatchedOrders = report.MatchedOrders.Select(ToDto).ToList(),
            UnmatchedPayPalTransactions = report.UnmatchedPayPalTransactions.Select(ToDto).ToList(),
            UnmatchedLocalOrders = report.UnmatchedLocalOrders.Select(ToDto).ToList()
        };

        return Results.Ok(response);
    }

    private static PayPalTransactionDto ToDto(TransactionRecord t) => new()
    {
        TransactionId = t.TransactionId,
        Amount = t.Amount,
        Currency = t.Currency,
        Status = t.Status,
        Date = t.Date
    };

    private static ReconciledOrderDto ToDto(ReconciledOrder o) => new()
    {
        OrderId = o.OrderId,
        CaptureId = o.CaptureId,
        CapturedAmount = o.CapturedAmount,
        CaptureStatus = o.CaptureStatus,
        CapturedAt = o.CapturedAt
    };
}
