using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using BlazorShared.Authorization;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.Infrastructure.PayPal;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.ReconciliationEndpoints;

public class ReconciliationEntry
{
    public string TransactionId { get; set; } = "";
    public string Status { get; set; } = "";
    public decimal Amount { get; set; }
    public decimal Fee { get; set; }
    public string CurrencyCode { get; set; } = "";
    public string TransactionDate { get; set; } = "";
    public int? MatchedOrderId { get; set; }
    public string? MatchedOrderStatus { get; set; }
}

public class ReconciliationMismatch
{
    public string Type { get; set; } = "";
    public string Description { get; set; } = "";
    public int? OrderId { get; set; }
    public string? PayPalTransactionId { get; set; }
}

public class ReconciliationResponse
{
    public string From { get; set; } = "";
    public string To { get; set; } = "";
    public int TotalPayPalTransactions { get; set; }
    public int MatchedOrders { get; set; }
    public string? Note { get; set; }
    public List<ReconciliationEntry> Transactions { get; set; } = new();
    public List<ReconciliationMismatch> Mismatches { get; set; } = new();
}

public class ReconciliationEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/reconciliation",
            [Authorize(Roles = Constants.Roles.ADMINISTRATORS,
                       AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
            async (string from, string to, HttpContext ctx,
                   IReadRepository<Order> orderRepo,
                   PayPalClient paypal) =>
            {
                if (!DateTimeOffset.TryParse(from, out var fromDate))
                    return Results.BadRequest(new { error = "Invalid 'from' date. Use ISO-8601 format." });
                if (!DateTimeOffset.TryParse(to, out var toDate))
                    return Results.BadRequest(new { error = "Invalid 'to' date. Use ISO-8601 format." });
                if (toDate <= fromDate)
                    return Results.BadRequest(new { error = "'to' must be after 'from'." });

                List<PayPalTransaction> paypalTx;
                string? paypalNote = null;
                try
                {
                    paypalTx = await paypal.GetTransactionsAsync(fromDate, toDate);
                }
                catch (PayPalException ex) when (
                    ex.PayPalName == "INVALID_REQUEST" &&
                    ex.Message.Contains("not available"))
                {
                    // PayPal reporting lags up to 3 hours; very recent dates may not be indexed yet.
                    paypalTx = new System.Collections.Generic.List<PayPalTransaction>();
                    paypalNote = "PayPal reporting data is not yet available for this date range (data typically lags 3 hours on sandbox). Showing eShop orders only.";
                }
                catch (PayPalException ex)
                {
                    return Results.UnprocessableEntity(new
                    {
                        error = $"PayPal reporting API error: {ex.Message}",
                        paypalCode = ex.PayPalName
                    });
                }

                // Load orders with captures in that window
                var allOrders = await orderRepo.ListAsync();
                var ordersWithCapture = allOrders
                    .Where(o => o.PayPalCaptureId != null)
                    .ToList();

                // Build lookup: PayPal capture/refund transaction ID → order
                var captureToOrder = ordersWithCapture
                    .Where(o => o.PayPalCaptureId != null)
                    .ToDictionary(o => o.PayPalCaptureId!, o => o);

                var entries = new List<ReconciliationEntry>();
                var mismatches = new List<ReconciliationMismatch>();

                // Track PayPal transaction IDs matched to orders
                var matchedTxIds = new HashSet<string>();
                var matchedOrderIds = new HashSet<int>();

                foreach (var tx in paypalTx)
                {
                    Order? matched = captureToOrder.TryGetValue(tx.TransactionId, out var o) ? o : null;

                    if (matched != null)
                    {
                        matchedTxIds.Add(tx.TransactionId);
                        matchedOrderIds.Add(matched.Id);
                    }
                    else
                    {
                        // PayPal knows about this transaction but our DB doesn't have a matching order
                        mismatches.Add(new ReconciliationMismatch
                        {
                            Type = "PAYPAL_ONLY",
                            Description = $"PayPal transaction {tx.TransactionId} (amount {tx.Amount:F2} {tx.CurrencyCode}) has no matching eShop order.",
                            PayPalTransactionId = tx.TransactionId
                        });
                    }

                    entries.Add(new ReconciliationEntry
                    {
                        TransactionId = tx.TransactionId,
                        Status = tx.Status,
                        Amount = tx.Amount,
                        Fee = tx.Fee,
                        CurrencyCode = tx.CurrencyCode,
                        TransactionDate = tx.TransactionDate.ToString("O"),
                        MatchedOrderId = matched?.Id,
                        MatchedOrderStatus = matched?.PaymentStatus.ToString()
                    });
                }

                // Orders with captures that aren't in the PayPal report
                foreach (var order in ordersWithCapture)
                {
                    if (!matchedOrderIds.Contains(order.Id))
                    {
                        mismatches.Add(new ReconciliationMismatch
                        {
                            Type = "ORDER_ONLY",
                            Description = $"Order {order.Id} has capture ID {order.PayPalCaptureId} but no matching PayPal transaction in the requested date range.",
                            OrderId = order.Id,
                            PayPalTransactionId = order.PayPalCaptureId
                        });
                    }
                }

                return Results.Ok(new ReconciliationResponse
                {
                    From = fromDate.ToString("O"),
                    To = toDate.ToString("O"),
                    TotalPayPalTransactions = paypalTx.Count,
                    MatchedOrders = matchedOrderIds.Count,
                    Note = paypalNote,
                    Transactions = entries,
                    Mismatches = mismatches
                });
            })
            .Produces<ReconciliationResponse>()
            .ProducesProblem(400)
            .WithTags("ReconciliationEndpoints");
    }
}
