using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Entities;
using Microsoft.eShopWeb.ApplicationCore.Entities.BuyerAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.PayPal;
using Microsoft.eShopWeb.Infrastructure.PayPal;
using Microsoft.eShopWeb.PublicApi.PaymentShared;
using Microsoft.Extensions.Options;
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

public class ReconciliationTransactionDto
{
    public string TransactionId { get; set; } = string.Empty;
    public decimal Amount { get; set; }
    public string CurrencyCode { get; set; } = string.Empty;
    public DateTimeOffset Date { get; set; }
    public string Status { get; set; } = string.Empty;
    public int? OrderId { get; set; }
}

public class ReconciliationResponse : BaseResponse
{
    public ReconciliationResponse(Guid correlationId) : base(correlationId) { }

    public DateTimeOffset From { get; set; }
    public DateTimeOffset To { get; set; }
    public int PayPalTransactionCount { get; set; }
    public List<ReconciliationTransactionDto> Matched { get; set; } = new();
    public List<ReconciliationTransactionDto> OnlyInPayPal { get; set; } = new();
    public List<ReconciliationTransactionDto> OnlyInEShop { get; set; } = new();
}

/// <summary>
/// Operator report: lines up PayPal's own transaction history for a date range against
/// eShop's local orders, so a payment either side doesn't know about is visible. PayPal's
/// reporting can lag live activity by up to a few hours, so a range covering activity just
/// created may legitimately show it as "only in eShop" - that is expected, not a bug.
/// </summary>
public class ReconciliationEndpoint : IEndpoint<IResult, ReconciliationRequest, PaymentDependencies>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/reconciliation",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS, AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (DateTimeOffset from, DateTimeOffset to,
             IRepository<Order> orderRepository, IRepository<Payment> paymentRepository, IRepository<Buyer> buyerRepository,
             IRepository<CatalogItem> catalogItemRepository, IPayPalClient payPalClient, IOptions<PayPalOptions> payPalOptions) =>
            {
                var request = new ReconciliationRequest(from, to);
                var deps = new PaymentDependencies(orderRepository, paymentRepository, buyerRepository, catalogItemRepository, payPalClient, payPalOptions.Value);
                return await HandleAsync(request, deps);
            })
            .Produces<ReconciliationResponse>()
            .WithTags("ReconciliationEndpoints");
    }

    public async Task<IResult> HandleAsync(ReconciliationRequest request, PaymentDependencies deps)
    {
        if (request.To <= request.From)
        {
            return Results.BadRequest("'to' must be after 'from'.");
        }

        var response = new ReconciliationResponse(request.CorrelationId()) { From = request.From, To = request.To };

        IReadOnlyList<PayPalTransactionRecord> payPalTransactions;
        try
        {
            payPalTransactions = await deps.PayPalClient.SearchTransactionsAsync(request.From, request.To);
        }
        catch (PayPalApiException ex)
        {
            return Results.Problem(ex.Message, statusCode: 502, title: ex.ErrorName ?? "Could not fetch PayPal transaction history");
        }
        response.PayPalTransactionCount = payPalTransactions.Count;

        var allPayments = await deps.PaymentRepository.ListAsync();

        var localTransactions = new List<ReconciliationTransactionDto>();
        foreach (var payment in allPayments)
        {
            if (payment.PayPalCaptureId != null && payment.CapturedAt.HasValue && payment.CapturedAt.Value >= request.From && payment.CapturedAt.Value <= request.To)
            {
                localTransactions.Add(new ReconciliationTransactionDto
                {
                    TransactionId = payment.PayPalCaptureId,
                    Amount = payment.CapturedAmount ?? 0m,
                    CurrencyCode = payment.Currency,
                    Date = payment.CapturedAt.Value,
                    Status = payment.CaptureStatus ?? string.Empty,
                    OrderId = payment.OrderId
                });
            }

            foreach (var refund in payment.Refunds)
            {
                if (refund.CreatedAt >= request.From && refund.CreatedAt <= request.To)
                {
                    localTransactions.Add(new ReconciliationTransactionDto
                    {
                        TransactionId = refund.PayPalRefundId,
                        Amount = refund.Amount,
                        CurrencyCode = payment.Currency,
                        Date = refund.CreatedAt,
                        Status = refund.Status,
                        OrderId = payment.OrderId
                    });
                }
            }
        }

        var localById = localTransactions.ToDictionary(t => t.TransactionId, StringComparer.OrdinalIgnoreCase);
        var payPalById = payPalTransactions.ToDictionary(t => t.TransactionId, StringComparer.OrdinalIgnoreCase);

        foreach (var local in localTransactions)
        {
            if (payPalById.ContainsKey(local.TransactionId))
            {
                response.Matched.Add(local);
            }
            else
            {
                response.OnlyInEShop.Add(local);
            }
        }

        foreach (var remote in payPalTransactions)
        {
            if (!localById.ContainsKey(remote.TransactionId))
            {
                response.OnlyInPayPal.Add(new ReconciliationTransactionDto
                {
                    TransactionId = remote.TransactionId,
                    Amount = remote.Amount,
                    CurrencyCode = remote.CurrencyCode,
                    Date = remote.InitiationDate,
                    Status = remote.Status
                });
            }
        }

        return Results.Ok(response);
    }
}
