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

namespace Microsoft.eShopWeb.PublicApi.ReconciliationEndpoints;

/// <summary>
/// Operator report: lists PayPal's own record of transactions for a date range
/// (paging through the whole range) and lines them up against eShop orders, so a
/// transaction PayPal knows about and eShop doesn't — or the reverse — is visible.
/// Note: PayPal's reporting lags live activity, so very recent payments may
/// legitimately be absent from PayPal's side of the report.
/// </summary>
public class GetReconciliationEndpoint : IEndpoint<IResult, GetReconciliationRequest>
{
    private readonly IPayPalClient _payPalClient;
    private readonly IReadRepository<Payment> _paymentRepository;

    public GetReconciliationEndpoint(IPayPalClient payPalClient, IReadRepository<Payment> paymentRepository)
    {
        _payPalClient = payPalClient;
        _paymentRepository = paymentRepository;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/reconciliation",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS, AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (DateTimeOffset from, DateTimeOffset to) =>
            {
                return await HandleAsync(new GetReconciliationRequest { From = from, To = to });
            })
            .Produces<GetReconciliationResponse>()
            .WithTags("ReconciliationEndpoints");
    }

    public async Task<IResult> HandleAsync(GetReconciliationRequest request)
    {
        var response = new GetReconciliationResponse(request.CorrelationId())
        {
            From = request.From,
            To = request.To
        };

        var payPalTransactions = await _payPalClient.ListTransactionsAsync(request.From, request.To);
        var payments = await _paymentRepository.ListAsync(new PaymentsCreatedInRangeSpec(request.From, request.To));

        // Every PayPal-owned id we know about, mapped back to its eShop order.
        var knownIds = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        foreach (var payment in payments)
        {
            void Map(string? id)
            {
                if (!string.IsNullOrEmpty(id))
                {
                    knownIds[id] = payment.OrderId;
                }
            }

            Map(payment.PayPalOrderId);
            Map(payment.AuthorizationId);
            Map(payment.CaptureId);
            foreach (var refund in payment.Refunds)
            {
                Map(refund.PayPalRefundId);
            }
        }

        var matchedPaymentOrderIds = new HashSet<int>();

        foreach (var transaction in payPalTransactions)
        {
            int? orderId = null;
            if ((transaction.TransactionId is not null && knownIds.TryGetValue(transaction.TransactionId, out var byId)) ||
                (transaction.ReferenceId is not null && knownIds.TryGetValue(transaction.ReferenceId, out byId)))
            {
                orderId = byId;
                matchedPaymentOrderIds.Add(byId);
            }

            response.Transactions.Add(new ReconciliationTransactionDto
            {
                TransactionId = transaction.TransactionId,
                ReferenceId = transaction.ReferenceId,
                ReferenceIdType = transaction.ReferenceIdType,
                EventCode = transaction.EventCode,
                Status = transaction.Status,
                Amount = transaction.Amount,
                Currency = transaction.Currency,
                Fee = transaction.Fee,
                InitiationDate = transaction.InitiationDate,
                UpdatedDate = transaction.UpdatedDate,
                OrderId = orderId,
                Match = orderId is null ? "UnknownToEShop" : "Matched"
            });
        }

        // eShop payments in the range that PayPal's report does not mention at all.
        var reportedIds = new HashSet<string>(
            payPalTransactions
                .SelectMany(t => new[] { t.TransactionId, t.ReferenceId })
                .Where(id => !string.IsNullOrEmpty(id))
                .Select(id => id!),
            StringComparer.OrdinalIgnoreCase);

        foreach (var payment in payments)
        {
            var paymentIds = new[] { payment.PayPalOrderId, payment.AuthorizationId, payment.CaptureId }
                .Concat(payment.Refunds.Select(r => r.PayPalRefundId))
                .Where(id => !string.IsNullOrEmpty(id))
                .Select(id => id!);

            if (!paymentIds.Any(reportedIds.Contains))
            {
                response.MissingFromPayPal.Add(new MissingPaymentDto
                {
                    OrderId = payment.OrderId,
                    PaymentId = payment.Id,
                    AuthorizationId = payment.AuthorizationId,
                    CaptureId = payment.CaptureId,
                    AuthorizedAmount = payment.AuthorizedAmount,
                    CapturedAmount = payment.CapturedAmount,
                    Currency = payment.Currency,
                    CreatedAt = payment.CreatedAt
                });
            }
        }

        response.Summary = new ReconciliationSummaryDto
        {
            PayPalTransactionCount = response.Transactions.Count,
            MatchedCount = response.Transactions.Count(t => t.Match == "Matched"),
            UnknownToEShopCount = response.Transactions.Count(t => t.Match == "UnknownToEShop"),
            MissingFromPayPalCount = response.MissingFromPayPal.Count
        };

        return Results.Ok(response);
    }
}

public class GetReconciliationRequest : BaseRequest
{
    /// <summary>Range start, ISO-8601. The range must not exceed 31 days.</summary>
    public DateTimeOffset From { get; set; }

    /// <summary>Range end, ISO-8601.</summary>
    public DateTimeOffset To { get; set; }
}

public class GetReconciliationResponse : BaseResponse
{
    public GetReconciliationResponse(Guid correlationId) : base(correlationId) {}
    public GetReconciliationResponse() {}

    public DateTimeOffset From { get; set; }
    public DateTimeOffset To { get; set; }
    public List<ReconciliationTransactionDto> Transactions { get; set; } = new();
    public List<MissingPaymentDto> MissingFromPayPal { get; set; } = new();
    public ReconciliationSummaryDto Summary { get; set; } = new();
}

public class ReconciliationTransactionDto
{
    public string TransactionId { get; set; } = string.Empty;
    public string? ReferenceId { get; set; }
    public string? ReferenceIdType { get; set; }
    public string? EventCode { get; set; }
    public string? Status { get; set; }
    public decimal? Amount { get; set; }
    public string? Currency { get; set; }
    public decimal? Fee { get; set; }
    public DateTimeOffset? InitiationDate { get; set; }
    public DateTimeOffset? UpdatedDate { get; set; }
    public int? OrderId { get; set; }

    /// <summary>"Matched" when lined up with an eShop order, "UnknownToEShop" otherwise.</summary>
    public string Match { get; set; } = string.Empty;
}

public class MissingPaymentDto
{
    public int OrderId { get; set; }
    public int PaymentId { get; set; }
    public string? AuthorizationId { get; set; }
    public string? CaptureId { get; set; }
    public decimal AuthorizedAmount { get; set; }
    public decimal? CapturedAmount { get; set; }
    public string Currency { get; set; } = string.Empty;
    public DateTimeOffset CreatedAt { get; set; }
}

public class ReconciliationSummaryDto
{
    public int PayPalTransactionCount { get; set; }
    public int MatchedCount { get; set; }
    public int UnknownToEShopCount { get; set; }
    public int MissingFromPayPalCount { get; set; }
}
