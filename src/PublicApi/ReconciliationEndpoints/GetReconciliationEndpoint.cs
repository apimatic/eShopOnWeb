using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Specifications;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.ReconciliationEndpoints;

/// <summary>
/// Operator action: lists PayPal's own record of transactions for a date range and lines
/// them up against eShop orders, so discrepancies in either direction are visible.
/// Covers the whole range (all pages), not just the first page.
/// </summary>
public class GetReconciliationEndpoint : IEndpoint<IResult, GetReconciliationRequest, ClaimsPrincipal>
{
    private readonly IPaymentGateway _paymentGateway;
    private readonly IRepository<Payment> _paymentRepository;

    public GetReconciliationEndpoint(IPaymentGateway paymentGateway, IRepository<Payment> paymentRepository)
    {
        _paymentGateway = paymentGateway;
        _paymentRepository = paymentRepository;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/reconciliation",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS, AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (DateTimeOffset from, DateTimeOffset to, ClaimsPrincipal user) =>
            {
                return await HandleAsync(new GetReconciliationRequest { From = from, To = to }, user);
            })
            .Produces<GetReconciliationResponse>()
            .WithTags("ReconciliationEndpoints");
    }

    public async Task<IResult> HandleAsync(GetReconciliationRequest request, ClaimsPrincipal user)
    {
        var response = new GetReconciliationResponse(request.CorrelationId());

        if (request.To <= request.From)
        {
            return Results.BadRequest("'to' must be after 'from'.");
        }
        // The transaction_search_v1 spec limits the range to 31 days.
        if (request.To - request.From > TimeSpan.FromDays(31))
        {
            return Results.BadRequest("The date range must not exceed 31 days.");
        }

        IReadOnlyList<GatewayTransaction> transactions;
        try
        {
            transactions = await _paymentGateway.SearchTransactionsAsync(request.From, request.To);
        }
        catch (PaymentGatewayException ex)
        {
            return Results.UnprocessableEntity(new { error = ex.Message, gatewayError = ex.GatewayErrorName });
        }

        var payments = await _paymentRepository.ListAsync(new PaymentsInDateRangeSpecification(request.From, request.To));

        // Index every PayPal-owned id eShop knows about: holds, captures and refunds.
        var knownIds = new Dictionary<string, (int OrderId, int PaymentId, string Kind)>();
        foreach (var payment in payments)
        {
            if (payment.AuthorizationId is not null)
            {
                knownIds.TryAdd(payment.AuthorizationId, (payment.OrderId, payment.Id, "authorization"));
            }
            if (payment.CaptureId is not null)
            {
                knownIds.TryAdd(payment.CaptureId, (payment.OrderId, payment.Id, "capture"));
            }
            foreach (var refund in payment.Refunds)
            {
                if (refund.PayPalRefundId is not null)
                {
                    knownIds.TryAdd(refund.PayPalRefundId, (payment.OrderId, payment.Id, "refund"));
                }
            }
        }

        var payPalIds = transactions.Select(t => t.TransactionId).ToHashSet();

        response.From = request.From;
        response.To = request.To;
        response.Transactions = transactions.Select(t => new ReconciliationTransactionDto
        {
            TransactionId = t.TransactionId,
            EventCode = t.EventCode,
            Status = t.Status,
            Amount = t.Amount,
            Currency = t.Currency,
            FeeAmount = t.FeeAmount,
            InvoiceId = t.InvoiceId,
            CustomId = t.CustomId,
            InitiationDate = t.InitiationDate,
            MatchedOrderId = knownIds.TryGetValue(t.TransactionId, out var match) ? match.OrderId : null,
            MatchedPaymentId = knownIds.TryGetValue(t.TransactionId, out match) ? match.PaymentId : null,
            MatchKind = knownIds.TryGetValue(t.TransactionId, out match) ? match.Kind : null
        }).ToList();

        // eShop payments whose PayPal ids do not appear in PayPal's report for the range.
        response.EShopOnlyPayments = payments
            .Where(p => KnownIds(p).Any() && KnownIds(p).All(id => !payPalIds.Contains(id)))
            .Select(p => new EShopPaymentDto
            {
                OrderId = p.OrderId,
                PaymentId = p.Id,
                Status = p.Status.ToString(),
                Amount = p.Amount,
                Currency = p.Currency,
                AuthorizationId = p.AuthorizationId,
                CaptureId = p.CaptureId,
                RefundIds = p.Refunds.Select(r => r.PayPalRefundId).Where(id => id is not null).Cast<string>().ToList()
            })
            .ToList();

        response.TotalPayPalTransactions = response.Transactions.Count;
        response.UnmatchedPayPalTransactions = response.Transactions.Count(t => t.MatchedOrderId is null);
        response.UnmatchedEShopPayments = response.EShopOnlyPayments.Count;

        return Results.Ok(response);
    }

    private static IEnumerable<string> KnownIds(Payment payment)
    {
        if (payment.AuthorizationId is not null) yield return payment.AuthorizationId;
        if (payment.CaptureId is not null) yield return payment.CaptureId;
        foreach (var refund in payment.Refunds)
        {
            if (refund.PayPalRefundId is not null) yield return refund.PayPalRefundId;
        }
    }
}

public class GetReconciliationRequest : BaseRequest
{
    public DateTimeOffset From { get; set; }
    public DateTimeOffset To { get; set; }
}

public class GetReconciliationResponse : BaseResponse
{
    public GetReconciliationResponse(Guid correlationId) : base(correlationId) { }
    public GetReconciliationResponse() { }

    public DateTimeOffset From { get; set; }
    public DateTimeOffset To { get; set; }
    public int TotalPayPalTransactions { get; set; }
    public int UnmatchedPayPalTransactions { get; set; }
    public int UnmatchedEShopPayments { get; set; }
    public List<ReconciliationTransactionDto> Transactions { get; set; } = new();
    public List<EShopPaymentDto> EShopOnlyPayments { get; set; } = new();
}

public class ReconciliationTransactionDto
{
    public string TransactionId { get; set; } = string.Empty;
    public string? EventCode { get; set; }
    public string? Status { get; set; }
    public decimal? Amount { get; set; }
    public string? Currency { get; set; }
    public decimal? FeeAmount { get; set; }
    public string? InvoiceId { get; set; }
    public string? CustomId { get; set; }
    public DateTimeOffset? InitiationDate { get; set; }
    public int? MatchedOrderId { get; set; }
    public int? MatchedPaymentId { get; set; }
    public string? MatchKind { get; set; }
}

public class EShopPaymentDto
{
    public int OrderId { get; set; }
    public int PaymentId { get; set; }
    public string Status { get; set; } = string.Empty;
    public decimal Amount { get; set; }
    public string Currency { get; set; } = string.Empty;
    public string? AuthorizationId { get; set; }
    public string? CaptureId { get; set; }
    public List<string> RefundIds { get; set; } = new();
}
