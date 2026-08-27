using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Specifications;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

/// <summary>
/// Operator action: lines up PayPal's own record of transactions over a date range against
/// eShop orders, so a payment only one side knows about is visible. Covers the whole range.
/// </summary>
public class ReconciliationEndpoint : IEndpoint<IResult, ReconciliationRequest, IRepository<Payment>>
{
    private const string InvoicePrefix = "eshop-order-";

    private readonly IPaymentGateway _paymentGateway;

    public ReconciliationEndpoint(IPaymentGateway paymentGateway)
    {
        _paymentGateway = paymentGateway;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/reconciliation",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS, AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            ([FromQuery] DateTimeOffset from, [FromQuery] DateTimeOffset to, IRepository<Payment> paymentRepository) =>
            {
                return await HandleAsync(new ReconciliationRequest { From = from, To = to }, paymentRepository);
            })
            .Produces<ReconciliationResponse>()
            .WithTags("OrderEndpoints");
    }

    public async Task<IResult> HandleAsync(ReconciliationRequest request, IRepository<Payment> paymentRepository)
    {
        if (request.To <= request.From)
        {
            return Results.BadRequest("'to' must be after 'from'. Both must be ISO-8601 date-times.");
        }

        var transactions = await _paymentGateway.ListTransactionsAsync(request.From, request.To);
        var payments = await paymentRepository.ListAsync(new PaymentsForReconciliationSpec(request.To));

        // Index every PayPal-owned id eShop knows about, back to its order.
        var orderIdByPayPalId = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        foreach (var payment in payments)
        {
            if (payment.AuthorizationId is not null) orderIdByPayPalId[payment.AuthorizationId] = payment.OrderId;
            if (payment.PayPalOrderId is not null) orderIdByPayPalId[payment.PayPalOrderId] = payment.OrderId;
            if (payment.CaptureId is not null) orderIdByPayPalId[payment.CaptureId] = payment.OrderId;
            foreach (var refund in payment.Refunds)
            {
                orderIdByPayPalId[refund.PayPalRefundId] = payment.OrderId;
            }
        }

        var knownPayPalIds = new HashSet<string>(orderIdByPayPalId.Keys, StringComparer.OrdinalIgnoreCase);
        var matchedOrderIds = new HashSet<int>();

        var lines = transactions.Select(t =>
        {
            var orderId = MatchOrder(t.TransactionId, t.CustomId, t.InvoiceId, orderIdByPayPalId);
            if (orderId.HasValue)
            {
                matchedOrderIds.Add(orderId.Value);
            }
            return new ReconciliationTransactionDto
            {
                PayPalTransactionId = t.TransactionId,
                EventCode = t.EventCode,
                Status = t.Status,
                Amount = t.Amount,
                Fee = t.Fee,
                Currency = t.Currency,
                TransactionTime = t.TransactionTime,
                InvoiceId = t.InvoiceId,
                CustomId = t.CustomId,
                MatchedOrderId = orderId,
                MatchStatus = orderId.HasValue ? "Matched" : "MissingInEShop"
            };
        }).ToList();

        // eShop payments with activity inside the window that PayPal's report does not mention.
        var seenIds = new HashSet<string>(transactions.Select(t => t.TransactionId), StringComparer.OrdinalIgnoreCase);
        var missingInPayPal = payments
            .Where(p => HasActivityInWindow(p, request.From, request.To))
            .Where(p => p.AuthorizationId is not null
                        && !seenIds.Contains(p.AuthorizationId)
                        && (p.CaptureId is null || !seenIds.Contains(p.CaptureId))
                        && !p.Refunds.Any(r => seenIds.Contains(r.PayPalRefundId)))
            .Select(p => new ReconciliationPaymentDto
            {
                OrderId = p.OrderId,
                PaymentStatus = p.Status.ToString(),
                AuthorizationId = p.AuthorizationId,
                CaptureId = p.CaptureId,
                Amount = p.CapturedAmount ?? p.AuthorizedAmount,
                Currency = p.Currency,
                MatchStatus = "MissingInPayPal"
            })
            .ToList();

        var response = new ReconciliationResponse
        {
            From = request.From,
            To = request.To,
            Transactions = lines,
            UnmatchedEshopPayments = missingInPayPal,
            MatchedCount = lines.Count(l => l.MatchStatus == "Matched"),
            MissingInEShopCount = lines.Count(l => l.MatchStatus == "MissingInEShop"),
            MissingInPayPalCount = missingInPayPal.Count
        };
        return Results.Ok(response);
    }

    private static int? MatchOrder(string transactionId, string? customId, string? invoiceId,
        IReadOnlyDictionary<string, int> orderIdByPayPalId)
    {
        if (orderIdByPayPalId.TryGetValue(transactionId, out var orderId))
        {
            return orderId;
        }
        if (int.TryParse(customId, out var fromCustom))
        {
            return fromCustom;
        }
        if (invoiceId is not null && invoiceId.StartsWith(InvoicePrefix, StringComparison.OrdinalIgnoreCase))
        {
            // invoice ids look like "eshop-order-{orderId}-{unique suffix}"
            var remainder = invoiceId[InvoicePrefix.Length..];
            var digits = new string(remainder.TakeWhile(char.IsDigit).ToArray());
            if (int.TryParse(digits, out var fromInvoice))
            {
                return fromInvoice;
            }
        }
        return null;
    }

    private static bool HasActivityInWindow(Payment payment, DateTimeOffset from, DateTimeOffset to)
    {
        return (payment.CreatedAt >= from && payment.CreatedAt <= to)
            || (payment.CapturedAt >= from && payment.CapturedAt <= to)
            || payment.Refunds.Any(r => r.CreatedAt >= from && r.CreatedAt <= to);
    }
}

public class ReconciliationRequest : BaseRequest
{
    public DateTimeOffset From { get; set; }
    public DateTimeOffset To { get; set; }
}

public class ReconciliationTransactionDto
{
    public string PayPalTransactionId { get; set; } = string.Empty;
    public string? EventCode { get; set; }
    public string? Status { get; set; }
    public decimal? Amount { get; set; }
    public decimal? Fee { get; set; }
    public string? Currency { get; set; }
    public DateTimeOffset? TransactionTime { get; set; }
    public string? InvoiceId { get; set; }
    public string? CustomId { get; set; }
    public int? MatchedOrderId { get; set; }
    public string MatchStatus { get; set; } = string.Empty;
}

public class ReconciliationPaymentDto
{
    public int OrderId { get; set; }
    public string PaymentStatus { get; set; } = string.Empty;
    public string? AuthorizationId { get; set; }
    public string? CaptureId { get; set; }
    public decimal Amount { get; set; }
    public string Currency { get; set; } = string.Empty;
    public string MatchStatus { get; set; } = string.Empty;
}

public class ReconciliationResponse : BaseResponse
{
    public DateTimeOffset From { get; set; }
    public DateTimeOffset To { get; set; }
    public List<ReconciliationTransactionDto> Transactions { get; set; } = new();
    public List<ReconciliationPaymentDto> UnmatchedEshopPayments { get; set; } = new();
    public int MatchedCount { get; set; }
    public int MissingInEShopCount { get; set; }
    public int MissingInPayPalCount { get; set; }
}
