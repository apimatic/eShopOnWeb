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
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Payments;
using Microsoft.eShopWeb.ApplicationCore.Specifications;
using MinimalApi.Endpoint;
using BlazorShared.Authorization;

namespace Microsoft.eShopWeb.PublicApi.ReconciliationEndpoints;

/// <summary>
/// Operator action: a report listing PayPal's own record of transactions for a date range,
/// lined up against eShop orders, covering the whole range (not just the first page).
/// A payment PayPal knows about and eShop doesn't — or the reverse — is visible.
/// </summary>
public class ReconciliationEndpoint : IEndpoint<IResult, DateTimeOffset?, DateTimeOffset?>
{
    private const string OrderReferencePrefix = "eshop-order-";

    private readonly IRepository<Order> _orderRepository;
    private readonly IPayPalGateway _gateway;

    public ReconciliationEndpoint(IRepository<Order> orderRepository, IPayPalGateway gateway)
    {
        _orderRepository = orderRepository;
        _gateway = gateway;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/reconciliation",
            [Authorize(Roles = Constants.Roles.ADMINISTRATORS, AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (DateTimeOffset? from, DateTimeOffset? to) =>
            {
                return await HandleAsync(from, to);
            })
            .Produces<ReconciliationResponse>()
            .Produces(StatusCodes.Status400BadRequest)
            .WithTags("ReconciliationEndpoints");
    }

    public async Task<IResult> HandleAsync(DateTimeOffset? from, DateTimeOffset? to)
    {
        if (from is null || to is null)
            throw new ApplicationCore.Exceptions.PaymentStateException("Both 'from' and 'to' (ISO-8601 date-times) are required.");
        if (from.Value > to.Value)
            throw new ApplicationCore.Exceptions.PaymentStateException("'from' must be on or before 'to'.");
        if (to.Value - from.Value > TimeSpan.FromDays(31))
            throw new ApplicationCore.Exceptions.PaymentStateException("The requested range exceeds PayPal's maximum of 31 days.");

        var transactions = await _gateway.SearchTransactionsAsync(from.Value, to.Value);

        var orders = await _orderRepository.ListAsync(new ApplicationCore.Specifications.OrdersWithPaymentSpec());

        // Maps to line up PayPal transactions against eShop orders.
        var orderByCustomField = BuildOrderByCustomFieldMap(orders);
        var orderByPayPalReference = BuildOrderByPayPalReferenceMap(orders);

        var matchedOrderIds = new System.Collections.Generic.HashSet<int>();
        var referencedPayPalIds = new System.Collections.Generic.HashSet<string>(StringComparer.OrdinalIgnoreCase);

        var response = new ReconciliationResponse(Guid.NewGuid())
        {
            From = from.Value.ToUniversalTime().ToString("O"),
            To = to.Value.ToUniversalTime().ToString("O"),
            TotalPayPalTransactions = transactions.Count,
            PayPalTransactions = transactions.Select(t =>
            {
                var matchedOrderId = MatchOrder(t, orderByCustomField, orderByPayPalReference);
                if (matchedOrderId.HasValue)
                {
                    matchedOrderIds.Add(matchedOrderId.Value);
                    referencedPayPalIds.Add(t.TransactionId);
                    if (!string.IsNullOrEmpty(t.PayPalReferenceId))
                        referencedPayPalIds.Add(t.PayPalReferenceId);
                }

                return new PayPalTransactionDto
                {
                    TransactionId = t.TransactionId,
                    EventCode = t.EventCode,
                    Status = t.Status,
                    InitiationDate = t.InitiationDate,
                    Amount = t.Amount,
                    Fee = t.Fee,
                    Currency = t.Currency,
                    CustomField = t.CustomField,
                    InvoiceId = t.InvoiceId,
                    PayPalReferenceId = t.PayPalReferenceId,
                    PayPalReferenceIdType = t.PayPalReferenceIdType,
                    Matched = matchedOrderId.HasValue,
                    MatchedOrderId = matchedOrderId
                };
            }).ToList()
        };

        response.OrdersWithoutPayPalRecord = orders
            .Where(o => !matchedOrderIds.Contains(o.Id))
            .Where(o => !OrderIsReferenced(o, referencedPayPalIds))
            .Select(o => new UnmatchedOrderDto
            {
                OrderId = o.Id,
                OrderDate = o.OrderDate,
                Total = o.Total(),
                Status = o.Status.ToString()
            })
            .OrderBy(o => o.OrderId)
            .ToList();

        return Results.Ok(response);
    }

    private static int? MatchOrder(ApplicationCore.Payments.PayPalTransaction transaction,
        System.Collections.Generic.IReadOnlyDictionary<string, int> orderByCustomField,
        System.Collections.Generic.IReadOnlyDictionary<string, int> orderByPayPalReference)
    {
        var byCustom = TryMatchCustomField(transaction.CustomField, orderByCustomField)
                       ?? TryMatchCustomField(transaction.InvoiceId, orderByCustomField);
        if (byCustom.HasValue) return byCustom;

        if (!string.IsNullOrEmpty(transaction.PayPalReferenceId) &&
            orderByPayPalReference.TryGetValue(transaction.PayPalReferenceId, out var orderId))
        {
            return orderId;
        }

        return null;
    }

    private static int? TryMatchCustomField(string? value, System.Collections.Generic.IReadOnlyDictionary<string, int> map)
    {
        if (string.IsNullOrEmpty(value)) return null;
        var trimmed = value.Trim();
        if (!trimmed.StartsWith(OrderReferencePrefix, StringComparison.OrdinalIgnoreCase)) return null;

        // Reference is "eshop-order-<id>" or "eshop-order-<id>-<instance>"; parse the leading id.
        var idPart = trimmed[OrderReferencePrefix.Length..];
        var end = 0;
        while (end < idPart.Length && char.IsDigit(idPart[end])) end++;
        if (end == 0) return null;

        var orderIdText = idPart[..end];
        return int.TryParse(orderIdText, out var orderId) && map.ContainsKey(orderIdText) ? orderId : null;
    }

    private static System.Collections.Generic.IReadOnlyDictionary<string, int> BuildOrderByCustomFieldMap(
        System.Collections.Generic.IReadOnlyList<Order> orders)
    {
        var map = new System.Collections.Generic.Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        foreach (var order in orders)
        {
            map[$"{order.Id}"] = order.Id;
        }
        return map;
    }

    private static System.Collections.Generic.IReadOnlyDictionary<string, int> BuildOrderByPayPalReferenceMap(
        System.Collections.Generic.IReadOnlyList<Order> orders)
    {
        var map = new System.Collections.Generic.Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        foreach (var order in orders)
        {
            var payment = order.Payment;
            if (payment is null) continue;

            map.TryAdd(payment.PayPalOrderId, order.Id);
            if (!string.IsNullOrEmpty(payment.AuthorizationId))
                map.TryAdd(payment.AuthorizationId, order.Id);
            if (!string.IsNullOrEmpty(payment.CaptureId))
                map.TryAdd(payment.CaptureId, order.Id);
            foreach (var refund in payment.Refunds)
            {
                if (!string.IsNullOrEmpty(refund.PayPalRefundId))
                    map.TryAdd(refund.PayPalRefundId, order.Id);
            }
        }
        return map;
    }

    private static bool OrderIsReferenced(Order order, System.Collections.Generic.ISet<string> referencedPayPalIds)
    {
        var payment = order.Payment;
        if (payment is null) return false;

        if (!string.IsNullOrEmpty(payment.PayPalOrderId) && referencedPayPalIds.Contains(payment.PayPalOrderId))
            return true;
        if (!string.IsNullOrEmpty(payment.CaptureId) && referencedPayPalIds.Contains(payment.CaptureId))
            return true;

        return payment.Refunds.Any(r => !string.IsNullOrEmpty(r.PayPalRefundId) && referencedPayPalIds.Contains(r.PayPalRefundId));
    }
}