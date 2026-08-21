using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Interfaces.Payments;
using Microsoft.eShopWeb.ApplicationCore.Specifications;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.PaymentEndpoints;

/// <summary>
/// GET /api/reconciliation?from={from}&amp;to={to} — line PayPal's own transaction record for a date
/// range up against eShop orders, so a payment PayPal knows about but eShop doesn't (or the reverse)
/// is visible. Covers the whole range (every 31-day sub-window, every page). Administrator only.
///
/// Note: PayPal transaction reporting lags live activity, so a range covering payments just created
/// may legitimately come back empty — that is an expected sandbox result, not a missing capability.
/// </summary>
public class ReconciliationEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/reconciliation",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS, AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async (
                string? from,
                string? to,
                IRepository<Order> orderRepository,
                IPaymentProcessor processor,
                CancellationToken ct) =>
            {
                if (!TryParseIso(from, out var fromDate) || !TryParseIso(to, out var toDate))
                {
                    return Results.BadRequest(new { message = "from and to must be ISO-8601 date-times." });
                }
                if (toDate <= fromDate)
                {
                    return Results.BadRequest(new { message = "to must be after from." });
                }

                var transactions = await processor.SearchTransactionsAsync(fromDate, toDate, ct);
                var localOrders = await orderRepository.ListAsync(new OrdersWithPaymentSpecification(), ct);

                var ordersByReference = localOrders
                    .Where(o => !string.IsNullOrEmpty(o.Payment?.CustomReference))
                    .GroupBy(o => o.Payment!.CustomReference!)
                    .ToDictionary(g => g.Key, g => g.First());
                var ordersByPayPalId = localOrders
                    .Where(o => !string.IsNullOrEmpty(o.Payment?.PayPalOrderId))
                    .GroupBy(o => o.Payment!.PayPalOrderId!)
                    .ToDictionary(g => g.Key, g => g.First());

                var matched = new List<ReconciliationTransactionDto>();
                var inPayPalNotInEShop = new List<ReconciliationTransactionDto>();
                var matchedOrderIds = new HashSet<int>();

                foreach (var txn in transactions)
                {
                    var order = MatchOrder(txn, ordersByReference, ordersByPayPalId);
                    var dto = ToTransactionDto(txn, order?.Id);

                    if (order is not null)
                    {
                        matched.Add(dto);
                        matchedOrderIds.Add(order.Id);
                    }
                    else
                    {
                        inPayPalNotInEShop.Add(dto);
                    }
                }

                var inEShopNotInPayPal = localOrders
                    .Where(o => !matchedOrderIds.Contains(o.Id) && IsInRange(o, fromDate, toDate))
                    .Select(o => new ReconciliationOrderDto
                    {
                        OrderId = o.Id,
                        PaymentStatus = o.PaymentStatus.ToString(),
                        PayPalOrderId = o.Payment?.PayPalOrderId,
                        CaptureId = o.Payment?.CaptureId,
                        CapturedAmount = o.Payment?.CapturedAmount
                    })
                    .ToList();

                var response = new ReconciliationResponse
                {
                    From = fromDate,
                    To = toDate,
                    PayPalTransactionCount = transactions.Count,
                    MatchedCount = matched.Count,
                    Matched = matched,
                    InPayPalNotInEShop = inPayPalNotInEShop,
                    InEShopNotInPayPal = inEShopNotInPayPal
                };

                return Results.Ok(response);
            })
            .Produces<ReconciliationResponse>()
            .WithTags("OrderPaymentEndpoints");
    }

    private static Order? MatchOrder(PayPalTransaction txn,
        IReadOnlyDictionary<string, Order> ordersByReference,
        IReadOnlyDictionary<string, Order> ordersByPayPalId)
    {
        foreach (var reference in new[] { txn.InvoiceId, txn.CustomField })
        {
            if (!string.IsNullOrEmpty(reference) && ordersByReference.TryGetValue(reference, out var byRef))
            {
                return byRef;
            }
        }

        if (!string.IsNullOrEmpty(txn.ReferenceId) && ordersByPayPalId.TryGetValue(txn.ReferenceId, out var byPayPal))
        {
            return byPayPal;
        }

        return null;
    }

    private static ReconciliationTransactionDto ToTransactionDto(PayPalTransaction txn, int? matchedOrderId) =>
        new ReconciliationTransactionDto
        {
            TransactionId = txn.TransactionId,
            Status = txn.Status,
            Amount = txn.Amount,
            CurrencyCode = txn.CurrencyCode,
            Fee = txn.Fee?.ToString(CultureInfo.InvariantCulture),
            InvoiceId = txn.InvoiceId,
            CustomField = txn.CustomField,
            ReferenceId = txn.ReferenceId,
            MatchedOrderId = matchedOrderId
        };

    private static bool IsInRange(Order order, DateTimeOffset from, DateTimeOffset to)
    {
        bool Within(DateTimeOffset? value) => value is DateTimeOffset v && v >= from && v <= to;
        return Within(order.OrderDate) || Within(order.Payment?.AuthorizedAt) || Within(order.Payment?.CapturedAt);
    }

    private static bool TryParseIso(string? value, out DateTimeOffset result)
    {
        if (!string.IsNullOrWhiteSpace(value) &&
            DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out result))
        {
            return true;
        }
        result = default;
        return false;
    }
}
