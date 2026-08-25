using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Ardalis.GuardClauses;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.PayPal;
using Microsoft.eShopWeb.ApplicationCore.Specifications;

namespace Microsoft.eShopWeb.ApplicationCore.Services;

public class OrderPaymentService : IOrderPaymentService
{
    private static readonly TimeSpan RenewalBuffer = TimeSpan.FromSeconds(60);

    // PayPal-Request-Id idempotency is scoped to the whole (possibly shared) merchant account, not to this
    // app or order id alone - a bare "order-{id}" key can collide with an unrelated transaction from another
    // deployment using the same small integer ids. A salt generated once per process run keeps keys stable
    // across retries within a run (so double-clicks still dedupe) while avoiding cross-deployment collisions.
    private static readonly string RunScope = Guid.NewGuid().ToString("N")[..8];

    private readonly IRepository<Order> _orderRepository;
    private readonly IRepository<Entities.BuyerAggregate.Buyer> _buyerRepository;
    private readonly IPayPalPaymentGateway _gateway;

    public OrderPaymentService(
        IRepository<Order> orderRepository,
        IRepository<Entities.BuyerAggregate.Buyer> buyerRepository,
        IPayPalPaymentGateway gateway)
    {
        _orderRepository = orderRepository;
        _buyerRepository = buyerRepository;
        _gateway = gateway;
    }

    public async Task<Order> AuthorizePaymentAsync(int orderId, string buyerId, PayPalCardDetails? card, int? paymentMethodId, CancellationToken ct = default)
    {
        if (card is null && paymentMethodId is null)
        {
            throw new InvalidOrderStateException("Either card details or a saved paymentMethodId must be provided.");
        }
        if (card is not null && paymentMethodId is not null)
        {
            throw new InvalidOrderStateException("Provide either card details or a saved paymentMethodId, not both.");
        }

        var order = await GetOwnedOrderAsync(orderId, buyerId, ct);

        if (order.Status != OrderStatus.AwaitingPayment)
        {
            return order;
        }

        var amount = order.Total();
        var requestId = $"eshop-{RunScope}-order-{order.Id}-authorize";

        order.BeginPaymentAuthorization(requestId);

        var result = card is not null
            ? await _gateway.AuthorizeWithCardAsync(card, amount, requestId, ct)
            : await _gateway.AuthorizeWithVaultedCardAsync(await ResolveVaultIdAsync(buyerId, paymentMethodId!.Value, ct), amount, requestId, ct);

        order.CompletePaymentAuthorization(result.PayPalOrderId, result.AuthorizationId, result.Status, result.Amount, result.CurrencyCode, result.ExpiresAt);

        await _orderRepository.UpdateAsync(order, ct);
        return order;
    }

    public async Task<Order> FulfilOrderAsync(int orderId, CancellationToken ct = default)
    {
        var order = await GetOrderAsync(orderId, ct);

        if (order.Status is OrderStatus.Fulfilled or OrderStatus.PartiallyRefunded or OrderStatus.Refunded)
        {
            return order;
        }

        if (order.Status != OrderStatus.PaymentAuthorized)
        {
            throw new InvalidOrderStateException($"Order {orderId} is {order.Status} and cannot be fulfilled.");
        }

        var payment = order.Payment!;
        var authorizationId = payment.PayPalAuthorizationId!;

        var current = await _gateway.GetAuthorizationAsync(authorizationId, ct);
        if (IsTerminal(current.Status))
        {
            throw new AuthorizationRenewalFailedException(
                $"The authorization for order {orderId} is {current.Status} and can no longer be captured or renewed. The shopper must pay again.");
        }

        if (current.ExpiresAt.HasValue && current.ExpiresAt.Value <= DateTimeOffset.UtcNow.Add(RenewalBuffer))
        {
            var renewRequestId = $"eshop-{RunScope}-order-{orderId}-reauthorize";
            try
            {
                var renewed = await _gateway.ReauthorizeAsync(authorizationId, payment.AuthorizedAmount, renewRequestId, ct);
                authorizationId = renewed.AuthorizationId;
                payment.RecordReauthorization(renewed.AuthorizationId, renewed.Status, renewed.ExpiresAt);
            }
            catch (PayPalGatewayException ex)
            {
                throw new AuthorizationRenewalFailedException(
                    $"Order {orderId}'s authorization has expired and PayPal could not renew it: {ex.Message}", ex);
            }
        }

        var captureRequestId = $"eshop-{RunScope}-order-{orderId}-capture";
        var captured = await _gateway.CaptureAuthorizationAsync(authorizationId, captureRequestId, ct);

        order.MarkFulfilled(captured.CaptureId, captured.Status, captured.GrossAmount, captured.FeeAmount, captured.NetAmount);
        await _orderRepository.UpdateAsync(order, ct);
        return order;
    }

    public async Task<Order> CancelOrderAsync(int orderId, CancellationToken ct = default)
    {
        var order = await GetOrderAsync(orderId, ct);

        if (order.Status == OrderStatus.Cancelled)
        {
            return order;
        }

        if (order.Status == OrderStatus.PaymentAuthorized)
        {
            var voidRequestId = $"eshop-{RunScope}-order-{orderId}-void";
            await _gateway.VoidAuthorizationAsync(order.Payment!.PayPalAuthorizationId!, voidRequestId, ct);
        }

        order.MarkCancelled();
        await _orderRepository.UpdateAsync(order, ct);
        return order;
    }

    public async Task<(Order Order, OrderPaymentRefund Refund)> RefundOrderAsync(int orderId, string buyerId, decimal? amount, string idempotencyKey, CancellationToken ct = default)
    {
        Guard.Against.NullOrWhiteSpace(idempotencyKey, nameof(idempotencyKey));

        var order = await GetOwnedOrderAsync(orderId, buyerId, ct);

        if (order.Status != OrderStatus.Fulfilled && order.Status != OrderStatus.PartiallyRefunded)
        {
            throw new InvalidOrderStateException($"Order {orderId} is {order.Status} and has no captured payment to refund.");
        }

        var payment = order.Payment!;

        var existing = payment.FindRefundByIdempotencyKey(idempotencyKey);
        if (existing is not null)
        {
            return (order, existing);
        }

        var remaining = payment.CapturedAmount!.Value - payment.TotalRefunded;
        var requestedAmount = amount ?? remaining;

        if (requestedAmount <= 0 || requestedAmount > remaining)
        {
            throw new RefundExceedsCapturedAmountException(
                $"Requested refund of {requestedAmount} exceeds the {remaining} still available to refund on order {orderId}.");
        }

        var isFullRemaining = requestedAmount == remaining && payment.TotalRefunded == 0;
        var payPalRequestId = $"eshop-{RunScope}-order-{orderId}-refund-{idempotencyKey}";
        var result = await _gateway.RefundCaptureAsync(
            payment.PayPalCaptureId!,
            isFullRemaining ? null : requestedAmount,
            payPalRequestId,
            ct);

        var refund = order.RecordRefund(result.RefundId, result.Amount, result.Status, idempotencyKey);
        await _orderRepository.UpdateAsync(order, ct);
        return (order, refund);
    }

    public async Task<IReadOnlyList<Order>> GetOrdersForBuyerAsync(string buyerId, CancellationToken ct = default)
    {
        var spec = new CustomerOrdersWithPaymentSpecification(buyerId);
        return await _orderRepository.ListAsync(spec, ct);
    }

    public async Task<ReconciliationReport> GetReconciliationReportAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken ct = default)
    {
        var payPalTransactions = await _gateway.SearchTransactionsAsync(from, to, ct);
        var payPalIds = payPalTransactions.Select(t => t.TransactionId).ToHashSet(StringComparer.OrdinalIgnoreCase);

        var spec = new OrdersWithPaymentSpecification();
        var orders = await _orderRepository.ListAsync(spec, ct);

        var eShopEntries = new List<ReconciliationEntry>();
        foreach (var order in orders)
        {
            var payment = order.Payment;
            if (payment is null)
            {
                continue;
            }

            if (payment.PayPalAuthorizationId is not null && IsWithinRange(payment.CreatedAt, from, to))
            {
                eShopEntries.Add(new ReconciliationEntry(payment.PayPalAuthorizationId, "Authorization", payment.AuthorizedAmount, payment.CurrencyCode, payment.CreatedAt, order.Id));
            }
            if (payment.PayPalCaptureId is not null && payment.CapturedAt.HasValue && IsWithinRange(payment.CapturedAt.Value, from, to))
            {
                eShopEntries.Add(new ReconciliationEntry(payment.PayPalCaptureId, "Capture", payment.CapturedAmount ?? 0m, payment.CurrencyCode, payment.CapturedAt, order.Id));
            }
            foreach (var refund in payment.Refunds)
            {
                if (IsWithinRange(refund.CreatedAt, from, to))
                {
                    eShopEntries.Add(new ReconciliationEntry(refund.PayPalRefundId, "Refund", refund.Amount, payment.CurrencyCode, refund.CreatedAt, order.Id));
                }
            }
        }

        var eShopIds = eShopEntries.Select(e => e.TransactionId).ToHashSet(StringComparer.OrdinalIgnoreCase);

        var matched = eShopEntries.Where(e => payPalIds.Contains(e.TransactionId)).ToList();
        var missingFromPayPal = eShopEntries.Where(e => !payPalIds.Contains(e.TransactionId)).ToList();
        var missingFromEShop = payPalTransactions
            .Where(t => !eShopIds.Contains(t.TransactionId))
            .Select(t => new ReconciliationEntry(t.TransactionId, t.Status ?? "Unknown", t.Amount, t.CurrencyCode, t.InitiatedAt, null))
            .ToList();

        return new ReconciliationReport(from, to, matched, missingFromPayPal, missingFromEShop);
    }

    private async Task<Order> GetOrderAsync(int orderId, CancellationToken ct)
    {
        var spec = new OrderWithPaymentByIdSpec(orderId);
        var order = await _orderRepository.FirstOrDefaultAsync(spec, ct);
        if (order is null)
        {
            throw new OrderNotFoundException(orderId);
        }
        return order;
    }

    private async Task<Order> GetOwnedOrderAsync(int orderId, string buyerId, CancellationToken ct)
    {
        var order = await GetOrderAsync(orderId, ct);
        if (order.BuyerId != buyerId)
        {
            throw new OrderNotFoundException(orderId);
        }
        return order;
    }

    private async Task<string> ResolveVaultIdAsync(string buyerId, int paymentMethodId, CancellationToken ct)
    {
        var spec = new BuyerWithPaymentMethodsSpecification(buyerId);
        var buyer = await _buyerRepository.FirstOrDefaultAsync(spec, ct);
        var method = buyer?.PaymentMethods.FirstOrDefault(p => p.Id == paymentMethodId);
        if (method is null)
        {
            throw new PaymentMethodNotFoundException(paymentMethodId);
        }
        return method.PayPalVaultId;
    }

    private static bool IsTerminal(string status) =>
        string.Equals(status, "VOIDED", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(status, "DENIED", StringComparison.OrdinalIgnoreCase);

    private static bool IsWithinRange(DateTimeOffset value, DateTimeOffset from, DateTimeOffset to) => value >= from && value <= to;
}
