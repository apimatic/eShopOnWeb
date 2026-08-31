using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Models.Payments;
using Microsoft.eShopWeb.ApplicationCore.Specifications;
using Microsoft.Extensions.Logging;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.ReconciliationEndpoints;

/// <summary>
/// Operator report: lists PayPal's own record of transactions for a date range
/// (paging through the whole range) and lines them up against eShop orders, so a
/// transaction PayPal knows about and eShop doesn't — or the reverse — is visible.
/// </summary>
public class ReconciliationEndpoint : IEndpoint<IResult, ReconciliationRequest>
{
    private readonly IRepository<Order> _orderRepository;
    private readonly IRepository<OrderPayment> _paymentRepository;
    private readonly IPaymentGateway _paymentGateway;
    private readonly ILogger<ReconciliationEndpoint> _logger;

    public ReconciliationEndpoint(IRepository<Order> orderRepository,
        IRepository<OrderPayment> paymentRepository,
        IPaymentGateway paymentGateway,
        ILogger<ReconciliationEndpoint> logger)
    {
        _orderRepository = orderRepository;
        _paymentRepository = paymentRepository;
        _paymentGateway = paymentGateway;
        _logger = logger;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/reconciliation",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS,
                AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (DateTimeOffset from, DateTimeOffset to) => await HandleAsync(new ReconciliationRequest(from, to)))
            .Produces<ReconciliationResponse>()
            .WithTags("ReconciliationEndpoints");
    }

    public async Task<IResult> HandleAsync(ReconciliationRequest request)
    {
        if (request.To <= request.From)
        {
            return Results.BadRequest(new { message = "The 'to' date-time must be after 'from' (ISO-8601)." });
        }

        IReadOnlyList<ProviderTransaction> transactions;
        try
        {
            transactions = await _paymentGateway.ListTransactionsAsync(request.From, request.To);
        }
        catch (PayPalApiException ex)
        {
            _logger.LogWarning("Transaction search failed: {Error} {Issue} (debug {DebugId})",
                ex.ErrorName, ex.Issue, ex.DebugId);
            return Results.Problem(ex.Message, statusCode: StatusCodes.Status502BadGateway);
        }

        var payments = await _paymentRepository.ListAsync(
            new OrderPaymentsInRangeSpec(request.From, request.To));
        var orders = await _orderRepository.ListAsync(
            new CustomerOrdersInRangeSpec(request.From, request.To));

        // Index eShop state by every PayPal id we hold.
        var paymentsByPayPalId = new Dictionary<string, OrderPayment>();
        foreach (var payment in payments)
        {
            if (payment.AuthorizationId is not null) paymentsByPayPalId[payment.AuthorizationId] = payment;
            if (payment.CaptureId is not null) paymentsByPayPalId[payment.CaptureId] = payment;
            foreach (var refund in payment.Refunds) paymentsByPayPalId[refund.PayPalRefundId] = payment;
        }

        var ordersById = orders.ToDictionary(o => o.Id);
        var payPalTransactionIds = transactions.Select(t => t.TransactionId).ToHashSet();

        var lines = new List<ReconciliationLineDto>();

        foreach (var transaction in transactions)
        {
            OrderPayment? matchedPayment = null;
            if (transaction.TransactionId.Length > 0)
            {
                paymentsByPayPalId.TryGetValue(transaction.TransactionId, out matchedPayment);
            }

            int? orderId = matchedPayment?.OrderId ?? TryParseOrderId(transaction.InvoiceId)
                ?? TryParseOrderId(transaction.CustomField);
            if (matchedPayment is null && orderId.HasValue)
            {
                matchedPayment = payments.FirstOrDefault(p => p.OrderId == orderId.Value);
            }

            var knownOrder = orderId.HasValue && ordersById.ContainsKey(orderId.Value);

            lines.Add(new ReconciliationLineDto
            {
                Source = "paypal",
                PayPalTransactionId = transaction.TransactionId,
                EventCode = transaction.EventCode,
                TransactionStatus = transaction.Status,
                Amount = transaction.Amount,
                Currency = transaction.Currency,
                Fee = transaction.Fee,
                TransactionTime = transaction.InitiationDate,
                OrderId = orderId,
                Match = matchedPayment is not null || knownOrder ? "matched" : "missingInEShop"
            });
        }

        // eShop payments whose PayPal ids do not appear in PayPal's report for the range.
        foreach (var payment in payments)
        {
            var missingIds = new List<string>();
            if (payment.AuthorizationId is not null && !payPalTransactionIds.Contains(payment.AuthorizationId))
            {
                missingIds.Add(payment.AuthorizationId);
            }
            if (payment.CaptureId is not null && !payPalTransactionIds.Contains(payment.CaptureId))
            {
                missingIds.Add(payment.CaptureId);
            }
            missingIds.AddRange(payment.Refunds
                .Where(r => !payPalTransactionIds.Contains(r.PayPalRefundId))
                .Select(r => r.PayPalRefundId));

            if (missingIds.Count > 0)
            {
                lines.Add(new ReconciliationLineDto
                {
                    Source = "eshop",
                    PayPalTransactionId = string.Join(", ", missingIds),
                    Amount = payment.CapturedAmount ?? payment.Amount,
                    Currency = payment.Currency,
                    TransactionTime = payment.CreatedAt,
                    OrderId = payment.OrderId,
                    Match = "missingInPayPal"
                });
            }
        }

        return Results.Ok(new ReconciliationResponse(request.CorrelationId())
        {
            From = request.From,
            To = request.To,
            Lines = lines
                .OrderBy(l => l.TransactionTime)
                .ToList()
        });
    }

    private static int? TryParseOrderId(string? value)
    {
        const string prefix = "eshop-order-";
        if (value is null || !value.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        var digits = value[prefix.Length..].TakeWhile(char.IsDigit).ToArray();
        return digits.Length > 0 && int.TryParse(new string(digits), out var orderId) ? orderId : null;
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

public class ReconciliationLineDto
{
    /// <summary>"paypal" for PayPal-reported transactions, "eshop" for eShop-side payments.</summary>
    public string Source { get; set; } = string.Empty;
    public string PayPalTransactionId { get; set; } = string.Empty;
    public string? EventCode { get; set; }
    public string? TransactionStatus { get; set; }
    public decimal? Amount { get; set; }
    public string? Currency { get; set; }
    public decimal? Fee { get; set; }
    public DateTimeOffset? TransactionTime { get; set; }
    public int? OrderId { get; set; }

    /// <summary>"matched", "missingInEShop" (PayPal knows it, eShop doesn't) or "missingInPayPal" (reverse).</summary>
    public string Match { get; set; } = string.Empty;
}

public class ReconciliationResponse : BaseResponse
{
    public ReconciliationResponse(Guid correlationId) : base(correlationId) { }
    public ReconciliationResponse() { }

    public DateTimeOffset From { get; set; }
    public DateTimeOffset To { get; set; }
    public List<ReconciliationLineDto> Lines { get; set; } = new();
}
