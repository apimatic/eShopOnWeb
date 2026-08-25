using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using BlazorShared.Authorization;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Specifications;
using Microsoft.eShopWeb.PublicApi.PayPal;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

public class ReconciliationEndpoint : IEndpoint<IResult, ReconciliationRequest, IRepository<Order>>
{
    private readonly IPayPalService _payPal;

    public ReconciliationEndpoint(IPayPalService payPal) => _payPal = payPal;

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/reconciliation",
            [Authorize(Roles = Constants.Roles.ADMINISTRATORS, AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
            async (string from, string to,
                   IRepository<OrderPayment> paymentRepository,
                   HttpContext ctx) =>
            {
                if (string.IsNullOrEmpty(from) || string.IsNullOrEmpty(to))
                    return Results.BadRequest(new { error = "from and to are required." });

                if (!DateTimeOffset.TryParse(from, out var fromDate) || !DateTimeOffset.TryParse(to, out var toDate))
                    return Results.BadRequest(new { error = "from and to must be valid ISO-8601 date-times." });

                List<TransactionRecord> transactions;
                try
                {
                    transactions = new List<TransactionRecord>(
                        await _payPal.SearchTransactionsAsync(
                            from, to, ctx.RequestAborted));
                }
                catch (PayPalException ex)
                {
                    return Results.BadRequest(new { error = ex.Message });
                }

                // Load all payments to match against transactions
                var allPayments = await paymentRepository.ListAsync();

                var paymentByPayPalOrderId = new Dictionary<string, OrderPayment>(System.StringComparer.OrdinalIgnoreCase);
                foreach (var p in allPayments)
                {
                    if (!string.IsNullOrEmpty(p.PayPalOrderId))
                        paymentByPayPalOrderId[p.PayPalOrderId] = p;
                }

                var reconciled = new List<object>();
                var txIds = new HashSet<string>(System.StringComparer.OrdinalIgnoreCase);

                foreach (var tx in transactions)
                {
                    var txId = tx.TransactionId ?? "";
                    txIds.Add(txId);

                    OrderPayment? matchedPayment = null;
                    if (!string.IsNullOrEmpty(tx.PayPalReferenceId))
                        paymentByPayPalOrderId.TryGetValue(tx.PayPalReferenceId, out matchedPayment);

                    reconciled.Add(new
                    {
                        transactionId = tx.TransactionId,
                        amount = tx.Amount,
                        currency = tx.Currency,
                        status = tx.Status,
                        payPalReferenceId = tx.PayPalReferenceId,
                        referenceType = tx.ReferenceType,
                        eShopOrderId = matchedPayment?.OrderId,
                        eShopPaymentStatus = matchedPayment?.Status.ToString(),
                        matchStatus = matchedPayment != null ? "Matched" : "PayPalOnly"
                    });
                }

                // Find eShop payments not in PayPal results
                var missing = new List<object>();
                foreach (var payment in allPayments)
                {
                    if (string.IsNullOrEmpty(payment.PayPalOrderId)) continue;
                    if (payment.Status == OrderPaymentStatus.PendingPayment) continue;
                    if (!string.IsNullOrEmpty(payment.CaptureId) && txIds.Contains(payment.CaptureId)) continue;
                    if (!string.IsNullOrEmpty(payment.AuthorizationId) && txIds.Contains(payment.AuthorizationId)) continue;

                    missing.Add(new
                    {
                        eShopOrderId = payment.OrderId,
                        eShopPaymentStatus = payment.Status.ToString(),
                        payPalOrderId = payment.PayPalOrderId,
                        authorizationId = payment.AuthorizationId,
                        captureId = payment.CaptureId,
                        matchStatus = "EShopOnly"
                    });
                }

                return Results.Ok(new
                {
                    from,
                    to,
                    transactionCount = transactions.Count,
                    transactions = reconciled,
                    eShopOnlyCount = missing.Count,
                    eShopOnly = missing
                });
            })
            .WithTags("OrderEndpoints");
    }

    public Task<IResult> HandleAsync(ReconciliationRequest request, IRepository<Order> repository)
        => Task.FromResult(Results.StatusCode(501) as IResult);
}
