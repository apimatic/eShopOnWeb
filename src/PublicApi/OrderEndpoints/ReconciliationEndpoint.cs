using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Specifications;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

/// <summary>
/// Operator action: lists PayPal's own record of transactions over a date range (the whole
/// range, all pages) lined up against eShop orders, so a mismatch in either direction is visible.
/// </summary>
public class ReconciliationEndpoint : IEndpoint<IResult, ReconciliationRequest>
{
    private readonly IPaymentGateway _paymentGateway;
    private readonly IReadRepository<Payment> _paymentRepository;

    public ReconciliationEndpoint(IPaymentGateway paymentGateway, IReadRepository<Payment> paymentRepository)
    {
        _paymentGateway = paymentGateway;
        _paymentRepository = paymentRepository;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/reconciliation",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS,
                AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (DateTimeOffset from, DateTimeOffset to) =>
            {
                return await HandleAsync(new ReconciliationRequest { From = from, To = to });
            })
            .Produces<ReconciliationResponse>()
            .WithTags("OrderEndpoints");
    }

    public async Task<IResult> HandleAsync(ReconciliationRequest request)
    {
        var response = new ReconciliationResponse
        {
            From = request.From,
            To = request.To
        };

        var transactions = await _paymentGateway.SearchTransactionsAsync(request.From, request.To);
        var localPayments = await _paymentRepository.ListAsync(
            new PaymentsCreatedInRangeSpec(request.From, request.To));

        var matchedPaymentIds = new HashSet<int>();

        foreach (var transaction in transactions)
        {
            var match = FindMatch(transaction, localPayments);
            if (match is not null)
            {
                matchedPaymentIds.Add(match.Id);
            }

            response.Transactions.Add(new ReconciliationTransactionDto
            {
                TransactionId = transaction.TransactionId,
                ReferenceId = transaction.ReferenceId,
                ReferenceType = transaction.ReferenceType,
                Amount = transaction.Amount,
                Fee = transaction.Fee,
                Status = transaction.Status,
                InitiatedAt = transaction.InitiatedAt,
                InvoiceId = transaction.InvoiceId,
                CustomId = transaction.CustomId,
                MatchedOrderId = match?.OrderId,
                MatchedPaymentId = match?.Id
            });
        }

        // The reverse direction: payments eShop knows about that PayPal's report does not.
        response.UnmatchedLocalPayments = localPayments
            .Where(p => !matchedPaymentIds.Contains(p.Id))
            .Select(p => new UnmatchedLocalPaymentDto
            {
                PaymentId = p.Id,
                OrderId = p.OrderId,
                PayPalOrderId = p.PayPalOrderId,
                AuthorizationId = p.AuthorizationId,
                CaptureId = p.CaptureId,
                Amount = p.CapturedAmount ?? p.AuthorizedAmount,
                Currency = p.Currency
            })
            .ToList();

        return Results.Ok(response);
    }

    // Matches only on exact PayPal-owned ids (order/authorization/capture/refund) — never on
    // invoice/custom id prefixes, which can collide with other integrations on a shared merchant.
    private static Payment? FindMatch(GatewayTransaction transaction, IReadOnlyCollection<Payment> localPayments)
    {
        return localPayments.FirstOrDefault(p =>
            (transaction.ReferenceId is not null &&
                (transaction.ReferenceId == p.PayPalOrderId ||
                 transaction.ReferenceId == p.AuthorizationId ||
                 transaction.ReferenceId == p.CaptureId)) ||
            transaction.TransactionId == p.PayPalOrderId ||
            transaction.TransactionId == p.AuthorizationId ||
            transaction.TransactionId == p.CaptureId ||
            p.Refunds.Any(r => r.RefundId == transaction.TransactionId));
    }
}

public class ReconciliationRequest : BaseRequest
{
    public DateTimeOffset From { get; set; }
    public DateTimeOffset To { get; set; }
}

public class ReconciliationResponse : BaseResponse
{
    public DateTimeOffset From { get; set; }
    public DateTimeOffset To { get; set; }
    public List<ReconciliationTransactionDto> Transactions { get; set; } = new List<ReconciliationTransactionDto>();
    public List<UnmatchedLocalPaymentDto> UnmatchedLocalPayments { get; set; } = new List<UnmatchedLocalPaymentDto>();
}

public class ReconciliationTransactionDto
{
    public string TransactionId { get; set; } = string.Empty;
    public string? ReferenceId { get; set; }
    public string? ReferenceType { get; set; }
    public decimal? Amount { get; set; }
    public decimal? Fee { get; set; }
    public string? Status { get; set; }
    public DateTimeOffset? InitiatedAt { get; set; }
    public string? InvoiceId { get; set; }
    public string? CustomId { get; set; }
    public int? MatchedOrderId { get; set; }
    public int? MatchedPaymentId { get; set; }
}

public class UnmatchedLocalPaymentDto
{
    public int PaymentId { get; set; }
    public int OrderId { get; set; }
    public string? PayPalOrderId { get; set; }
    public string? AuthorizationId { get; set; }
    public string? CaptureId { get; set; }
    public decimal Amount { get; set; }
    public string Currency { get; set; } = string.Empty;
}
