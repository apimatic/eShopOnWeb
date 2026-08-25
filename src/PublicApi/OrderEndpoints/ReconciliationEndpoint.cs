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
using Microsoft.eShopWeb.ApplicationCore.Specifications;
using Microsoft.eShopWeb.PublicApi.PayPal;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

public class ReconciliationEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/reconciliation",
            [Authorize(Roles = Constants.Roles.ADMINISTRATORS, AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
            async (string from, string to,
                   IReadRepository<Order> orderRepo,
                   IPayPalService payPal) =>
            {
                if (!DateTimeOffset.TryParse(from, out var fromDate) ||
                    !DateTimeOffset.TryParse(to, out var toDate))
                    return Results.BadRequest(new { error = "from and to must be valid ISO-8601 date/time strings." });

                if (toDate <= fromDate)
                    return Results.BadRequest(new { error = "to must be after from." });

                IReadOnlyList<TransactionItem> payPalTxns;
                try
                {
                    payPalTxns = await payPal.SearchTransactionsAsync(
                        fromDate.ToString("yyyy-MM-ddTHH:mm:sszzz"),
                        toDate.ToString("yyyy-MM-ddTHH:mm:sszzz"));
                }
                catch (PayPalException ex)
                {
                    return Results.Problem($"PayPal transaction search failed: {ex.Message}", statusCode: 502);
                }

                // All captured/fulfilled orders in the window
                var orders = await orderRepo.ListAsync(new AllOrdersWithPaymentSpec());
                var ordersInWindow = orders.Where(o =>
                    o.OrderDate >= fromDate && o.OrderDate <= toDate &&
                    (o.Status == OrderStatus.Fulfilled ||
                     o.Status == OrderStatus.PartiallyRefunded ||
                     o.Status == OrderStatus.FullyRefunded)).ToList();

                // Cross-reference: find orders whose captureId is in PayPal transactions
                var payPalCaptureIds = payPalTxns
                    .Where(t => !string.IsNullOrEmpty(t.TransactionId))
                    .Select(t => t.TransactionId!)
                    .ToHashSet(StringComparer.OrdinalIgnoreCase);

                var matched = ordersInWindow
                    .Where(o => o.CaptureId != null && payPalCaptureIds.Contains(o.CaptureId))
                    .Select(o => new { orderId = o.Id, captureId = o.CaptureId, capturedAmount = o.CapturedAmount, status = o.Status.ToString() })
                    .ToList();

                var unmatchedOrders = ordersInWindow
                    .Where(o => o.CaptureId == null || !payPalCaptureIds.Contains(o.CaptureId))
                    .Select(o => new { orderId = o.Id, captureId = o.CaptureId, capturedAmount = o.CapturedAmount, status = o.Status.ToString() })
                    .ToList();

                var eShopCaptureIds = ordersInWindow
                    .Where(o => o.CaptureId != null)
                    .Select(o => o.CaptureId!)
                    .ToHashSet(StringComparer.OrdinalIgnoreCase);

                var unmatchedPayPal = payPalTxns
                    .Where(t => t.TransactionId != null && !eShopCaptureIds.Contains(t.TransactionId))
                    .ToList();

                return Results.Ok(new
                {
                    from = fromDate,
                    to = toDate,
                    payPalTransactionCount = payPalTxns.Count,
                    eShopOrdersInWindow = ordersInWindow.Count,
                    matched,
                    unmatchedOrders,
                    unmatchedPayPalTransactions = unmatchedPayPal.Select(t => new
                    {
                        transactionId = t.TransactionId,
                        amount = t.Amount,
                        currency = t.Currency,
                        status = t.Status,
                        initiationDate = t.InitiationDate,
                        eventCode = t.EventCode
                    })
                });
            })
            .WithTags("OrderEndpoints");
    }
}
