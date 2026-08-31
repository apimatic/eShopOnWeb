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
using Microsoft.eShopWeb.ApplicationCore.Models.Payments;
using Microsoft.eShopWeb.ApplicationCore.Specifications;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

/// <summary>
/// Operator action: lists PayPal's own record of transactions for a date range and lines
/// them up against eShop orders, so a payment only one side knows about is visible.
/// PayPal's transaction search covers at most 31 days per call and pages its results;
/// this endpoint chunks and pages so the whole requested range is covered.
/// </summary>
public class ReconciliationEndpoint : IEndpoint<IResult, ReconciliationRequest>
{
    private const int MaxWindowDays = 31;
    private const int PageSize = 100;

    private readonly IRepository<OrderPayment> _paymentRepository;
    private readonly IPaymentGateway _paymentGateway;

    public ReconciliationEndpoint(IRepository<OrderPayment> paymentRepository, IPaymentGateway paymentGateway)
    {
        _paymentRepository = paymentRepository;
        _paymentGateway = paymentGateway;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/reconciliation",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS, AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (DateTimeOffset from, DateTimeOffset to) =>
            {
                return await HandleAsync(new ReconciliationRequest(from, to));
            })
            .Produces<ReconciliationResponse>()
            .WithTags("OrderEndpoints");
    }

    public async Task<IResult> HandleAsync(ReconciliationRequest request)
    {
        if (request.To <= request.From)
        {
            return Results.BadRequest("The 'to' date-time must be after the 'from' date-time. Both must be ISO-8601 date-times.");
        }

        var payPalTransactions = await FetchAllTransactions(request.From, request.To);
        var localPayments = await _paymentRepository.ListAsync(new OrderPaymentsCreatedBetweenSpec(request.From, request.To));

        var byAuthorizationId = localPayments.Where(p => p.AuthorizationId != null)
            .ToDictionary(p => p.AuthorizationId!, p => p);
        var byCaptureId = localPayments.Where(p => p.CaptureId != null)
            .ToDictionary(p => p.CaptureId!, p => p);
        var byRefundId = localPayments.SelectMany(p => p.Refunds.Select(r => (r.PayPalRefundId, Payment: p)))
            .ToDictionary(x => x.PayPalRefundId, x => x.Payment);
        var byReference = localPayments.ToDictionary(p => ReferenceFor(p.OrderId), p => p);

        var seenPaymentIds = new HashSet<int>();
        var entries = new List<ReconciliationEntry>();

        foreach (var txn in payPalTransactions)
        {
            var payment = Match(txn, byAuthorizationId, byCaptureId, byRefundId, byReference);
            if (payment != null)
            {
                seenPaymentIds.Add(payment.Id);
            }

            entries.Add(new ReconciliationEntry
            {
                PayPalTransactionId = txn.TransactionId,
                PayPalReferenceId = txn.ReferenceId,
                EventCode = txn.EventCode,
                Status = txn.Status,
                Amount = txn.Amount,
                Currency = txn.Currency,
                Fee = txn.Fee,
                InvoiceId = txn.InvoiceId,
                InitiatedAt = txn.InitiationDate,
                MatchedOrderId = payment?.OrderId,
                MatchedPaymentId = payment?.Id,
                MatchStatus = payment == null ? "paypalOnly" : "matched"
            });
        }

        var localOnly = localPayments
            .Where(p => !seenPaymentIds.Contains(p.Id))
            .Select(p => new ReconciliationLocalEntry
            {
                OrderId = p.OrderId,
                PaymentId = p.Id,
                AuthorizationId = p.AuthorizationId,
                CaptureId = p.CaptureId,
                Amount = p.CapturedAmount ?? p.Amount,
                Currency = p.Currency,
                CreatedAt = p.CreatedAt,
                MatchStatus = "eshopOnly"
            })
            .ToList();

        return Results.Ok(new ReconciliationResponse(request.CorrelationId())
        {
            From = request.From,
            To = request.To,
            Transactions = entries,
            UnmatchedLocalPayments = localOnly,
            Note = "PayPal transaction reporting lags live activity (up to a few hours); very recent payments may legitimately appear as eshopOnly."
        });
    }

    private async Task<List<PayPalTransaction>> FetchAllTransactions(DateTimeOffset from, DateTimeOffset to)
    {
        var all = new List<PayPalTransaction>();
        var windowStart = from;
        while (windowStart < to)
        {
            var windowEnd = windowStart.AddDays(MaxWindowDays) < to ? windowStart.AddDays(MaxWindowDays) : to;

            var page = 1;
            while (true)
            {
                var result = await _paymentGateway.SearchTransactionsAsync(windowStart, windowEnd, page, PageSize);
                all.AddRange(result.Transactions);
                if (result.Transactions.Count == 0 || page >= result.TotalPages)
                {
                    break;
                }
                page++;
            }

            windowStart = windowEnd;
        }
        return all;
    }

    private static string ReferenceFor(int orderId) => $"eshop-order-{orderId}";

    private static OrderPayment? Match(
        PayPalTransaction txn,
        Dictionary<string, OrderPayment> byAuthorizationId,
        Dictionary<string, OrderPayment> byCaptureId,
        Dictionary<string, OrderPayment> byRefundId,
        Dictionary<string, OrderPayment> byReference)
    {
        if (byCaptureId.TryGetValue(txn.TransactionId, out var byCapture)) return byCapture;
        if (byAuthorizationId.TryGetValue(txn.TransactionId, out var byAuth)) return byAuth;
        if (byRefundId.TryGetValue(txn.TransactionId, out var byRefund)) return byRefund;
        if (txn.ReferenceId != null && byCaptureId.TryGetValue(txn.ReferenceId, out var byRefCapture)) return byRefCapture;
        if (txn.ReferenceId != null && byAuthorizationId.TryGetValue(txn.ReferenceId, out var byRefAuth)) return byRefAuth;
        if (txn.InvoiceId != null && byReference.TryGetValue(txn.InvoiceId, out var byInvoice)) return byInvoice;
        if (txn.CustomField != null && byReference.TryGetValue(txn.CustomField, out var byCustom)) return byCustom;
        return null;
    }
}
