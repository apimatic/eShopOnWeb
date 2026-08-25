using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using BlazorShared;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.Infrastructure.PayPal;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

public class ReconciliationEndpoint : IEndpoint<IResult, ReconciliationRequest, IRepository<Payment>>
{
    private readonly PayPalClient _payPalClient;
    private readonly IRepository<Order> _orderRepository;

    public ReconciliationEndpoint(PayPalClient payPalClient, IRepository<Order> orderRepository)
    {
        _payPalClient = payPalClient;
        _orderRepository = orderRepository;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/reconciliation",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS, AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
            async (string from, string to, IRepository<Payment> paymentRepository) =>
            {
                return await HandleAsync(new ReconciliationRequest { From = from, To = to }, paymentRepository);
            })
            .Produces<ReconciliationResponse>(200)
            .Produces(400)
            .WithTags("OrderEndpoints");
    }

    public async Task<IResult> HandleAsync(ReconciliationRequest request, IRepository<Payment> paymentRepository)
    {
        if (!DateTimeOffset.TryParse(request.From, out var fromDate))
            return Results.BadRequest(new { error = "Invalid 'from' date format. Use ISO-8601." });
        if (!DateTimeOffset.TryParse(request.To, out var toDate))
            return Results.BadRequest(new { error = "Invalid 'to' date format. Use ISO-8601." });
        if (fromDate >= toDate)
            return Results.BadRequest(new { error = "'from' must be before 'to'." });

        try
        {
            // Fetch all PayPal transactions for the range
            var payPalTransactions = await _payPalClient.GetAllTransactionsAsync(fromDate, toDate);

            // Fetch all local payment records
            var allPayments = await paymentRepository.ListAsync();

            // Build lookup by PayPal order ID and capture/auth IDs
            var localByPayPalOrderId = allPayments
                .Where(p => !string.IsNullOrEmpty(p.PayPalOrderId))
                .GroupBy(p => p.PayPalOrderId)
                .ToDictionary(g => g.Key, g => g.First());

            var localByAuthId = allPayments
                .Where(p => !string.IsNullOrEmpty(p.AuthorizationId))
                .GroupBy(p => p.AuthorizationId)
                .ToDictionary(g => g.Key, g => g.First());

            var localByCaptureId = allPayments
                .Where(p => !string.IsNullOrEmpty(p.CaptureId))
                .GroupBy(p => p.CaptureId!)
                .ToDictionary(g => g.Key, g => g.First());

            var rows = new List<ReconciliationRow>();
            var matchedLocalIds = new HashSet<int>();

            foreach (var tx in payPalTransactions)
            {
                var txId = tx.TransactionInfo?.TransactionId ?? "";
                var refId = tx.TransactionInfo?.PayPalReferenceId ?? "";
                var amount = tx.TransactionInfo?.TransactionAmount?.Value ?? "";
                var status = tx.TransactionInfo?.TransactionStatus ?? "";
                var eventCode = tx.TransactionInfo?.TransactionEventCode ?? "";
                var txDate = tx.TransactionInfo?.TransactionInitiationDate ?? "";

                // Try to match to a local payment
                Payment? matchedPayment = null;
                if (!string.IsNullOrEmpty(refId))
                {
                    localByPayPalOrderId.TryGetValue(refId, out matchedPayment);
                    if (matchedPayment == null) localByAuthId.TryGetValue(refId, out matchedPayment);
                    if (matchedPayment == null) localByCaptureId.TryGetValue(refId, out matchedPayment);
                }
                if (matchedPayment == null)
                {
                    localByAuthId.TryGetValue(txId, out matchedPayment);
                    if (matchedPayment == null) localByCaptureId.TryGetValue(txId, out matchedPayment);
                }

                if (matchedPayment != null) matchedLocalIds.Add(matchedPayment.Id);

                rows.Add(new ReconciliationRow
                {
                    PayPalTransactionId = txId,
                    PayPalReferenceId = refId,
                    TransactionDate = txDate,
                    TransactionEventCode = eventCode,
                    Amount = amount,
                    TransactionStatus = status,
                    LocalOrderId = matchedPayment?.OrderId,
                    LocalPaymentStatus = matchedPayment != null ? GetLocalStatus(matchedPayment) : null,
                    MatchStatus = matchedPayment != null ? "Matched" : "PayPalOnly"
                });
            }

            // Local payments with no matching PayPal transaction in this range
            foreach (var payment in allPayments)
            {
                if (matchedLocalIds.Contains(payment.Id)) continue;
                if (payment.AuthorizedAt < fromDate || payment.AuthorizedAt > toDate) continue;

                rows.Add(new ReconciliationRow
                {
                    PayPalTransactionId = null,
                    PayPalReferenceId = payment.PayPalOrderId,
                    TransactionDate = null,
                    Amount = payment.CapturedAmount?.ToString("F2") ?? payment.AuthorizedAmount.ToString("F2"),
                    LocalOrderId = payment.OrderId,
                    LocalPaymentStatus = GetLocalStatus(payment),
                    MatchStatus = "LocalOnly"
                });
            }

            return Results.Ok(new ReconciliationResponse
            {
                From = fromDate,
                To = toDate,
                TotalPayPalTransactions = payPalTransactions.Count,
                TotalLocalPayments = allPayments.Count,
                Rows = rows
            });
        }
        catch (PayPalException ex)
        {
            return Results.Problem(
                detail: ex.Message,
                statusCode: ex.StatusCode > 0 ? ex.StatusCode : 502,
                title: "PayPalError",
                extensions: ex.DebugId != null
                    ? new Dictionary<string, object?> { ["debugId"] = ex.DebugId }
                    : null);
        }
    }

    private static string GetLocalStatus(Payment p)
    {
        if (!string.IsNullOrEmpty(p.CaptureId)) return "Captured";
        if (p.AuthorizationStatus == "VOIDED") return "Voided";
        return "Authorized";
    }
}
