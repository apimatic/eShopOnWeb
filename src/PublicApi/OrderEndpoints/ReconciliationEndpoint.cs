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

public class ReconciliationTransactionDto
{
    public string TransactionId { get; set; } = string.Empty;
    public string? EventCode { get; set; }
    public string? Status { get; set; }
    public decimal? Amount { get; set; }
    public string? Currency { get; set; }
    public decimal? Fee { get; set; }
    public DateTimeOffset? TransactionTime { get; set; }
    public int? MatchedOrderId { get; set; }
    public int? MatchedPaymentId { get; set; }
}

public class UnmatchedPaymentDto
{
    public int PaymentId { get; set; }
    public int OrderId { get; set; }
    public decimal Amount { get; set; }
    public string Currency { get; set; } = string.Empty;
    public List<string> ProviderTransactionIds { get; set; } = new();
}

public class ReconciliationResponse : BaseResponse
{
    public DateTimeOffset From { get; set; }
    public DateTimeOffset To { get; set; }
    public List<ReconciliationTransactionDto> Transactions { get; set; } = new();
    public List<UnmatchedPaymentDto> PaymentsWithoutProviderTransaction { get; set; } = new();
}

/// <summary>
/// Operator: lists PayPal's own record of transactions for a date range (the whole range, all
/// pages) lined up against eShop orders — a transaction PayPal knows about and eShop doesn't,
/// or the reverse, is visible. from/to are ISO-8601 date-times.
/// </summary>
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

public class ReconciliationEndpoint : IEndpoint<IResult, ReconciliationRequest, IPaymentService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/reconciliation",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS, AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (DateTimeOffset from, DateTimeOffset to, IPaymentService paymentService) =>
            {
                return await HandleAsync(new ReconciliationRequest(from, to), paymentService);
            })
            .Produces<ReconciliationResponse>()
            .WithTags("OrderEndpoints");
    }

    public async Task<IResult> HandleAsync(ReconciliationRequest request, IPaymentService paymentService)
    {
        if (request.To <= request.From)
        {
            return Results.BadRequest("'to' must be after 'from'.");
        }

        var report = await paymentService.ReconcileAsync(request.From, request.To, default);

        var response = new ReconciliationResponse
        {
            From = report.From,
            To = report.To,
            Transactions = report.Transactions.Select(t => new ReconciliationTransactionDto
            {
                TransactionId = t.TransactionId,
                EventCode = t.EventCode,
                Status = t.Status,
                Amount = t.Amount,
                Currency = t.Currency,
                Fee = t.Fee,
                TransactionTime = t.TransactionTime,
                MatchedOrderId = t.MatchedOrderId,
                MatchedPaymentId = t.MatchedPaymentId
            }).ToList(),
            PaymentsWithoutProviderTransaction = report.PaymentsWithoutProviderTransaction
                .Select(p => new UnmatchedPaymentDto
                {
                    PaymentId = p.PaymentId,
                    OrderId = p.OrderId,
                    Amount = p.Amount,
                    Currency = p.Currency,
                    ProviderTransactionIds = p.ProviderTransactionIds.ToList()
                }).ToList()
        };

        return Results.Ok(response);
    }
}
