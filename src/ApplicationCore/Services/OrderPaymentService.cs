using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Ardalis.GuardClauses;
using Microsoft.eShopWeb.ApplicationCore.Entities.BuyerAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.PaymentProcessing;
using Microsoft.eShopWeb.ApplicationCore.Specifications;

namespace Microsoft.eShopWeb.ApplicationCore.Services;

public class OrderPaymentService : IOrderPaymentService
{
    private readonly IRepository<Order> _orderRepository;
    private readonly IRepository<Buyer> _buyerRepository;
    private readonly IPaymentGateway _gateway;
    private readonly PaymentSettings _paymentSettings;

    public OrderPaymentService(IRepository<Order> orderRepository, IRepository<Buyer> buyerRepository, IPaymentGateway gateway, PaymentSettings paymentSettings)
    {
        _orderRepository = orderRepository;
        _buyerRepository = buyerRepository;
        _gateway = gateway;
        _paymentSettings = paymentSettings;
    }

    public async Task<Payment> AuthorizePaymentAsync(int orderId, string buyerId, CardDetails? card, int? savedPaymentMethodId, CancellationToken ct = default)
    {
        if ((card is null) == (savedPaymentMethodId is null))
        {
            throw new InvalidOrderStateException("Provide either card details or a saved payment method id, not both or neither.");
        }

        var order = await LoadOwnedOrderAsync(orderId, buyerId, ct);

        if (order.Status == OrderStatus.PaymentAuthorized || order.Status == OrderStatus.Fulfilled)
        {
            // Already authorized (or further along) - a double-click never authorizes twice.
            return order.Payment!;
        }

        if (order.Status != OrderStatus.AwaitingPayment)
        {
            throw new InvalidOrderStateException($"Order {orderId} is {order.Status} and cannot be paid.");
        }

        var amount = order.Total();
        var currency = _paymentSettings.Currency;

        // A fresh key per attempt: the order-status check above (not PayPal's dedup) is what makes a
        // same-order double-click a no-op once the first attempt has committed. A key that instead
        // stayed fixed per order would make PayPal replay a first attempt's outcome forever - including
        // a decline - blocking any legitimate retry after it.
        var idempotencyKey = $"authorize-order-{orderId}-{Guid.NewGuid():N}";

        AuthorizationResult result;
        int? paymentMethodId = null;

        if (card is not null)
        {
            result = await _gateway.AuthorizeWithCardAsync(amount, currency, card, idempotencyKey, ct);
        }
        else
        {
            var buyer = await _buyerRepository.FirstOrDefaultAsync(new BuyerWithPaymentMethodsSpec(buyerId), ct);
            var paymentMethod = buyer?.PaymentMethods.FirstOrDefault(pm => pm.Id == savedPaymentMethodId!.Value);
            if (paymentMethod is null)
            {
                throw new PaymentMethodNotFoundException(savedPaymentMethodId!.Value);
            }

            paymentMethodId = paymentMethod.Id;
            result = await _gateway.AuthorizeWithVaultedCardAsync(amount, currency, paymentMethod.VaultId, idempotencyKey, ct);
        }

        var payment = new Payment(order.Id, amount, currency);
        payment.RecordAuthorization(result.PayPalOrderId, result.AuthorizationId, result.Status, result.ExpiresAt, paymentMethodId);
        order.AttachPayment(payment);

        await _orderRepository.UpdateAsync(order, ct);
        return payment;
    }

    public async Task<Payment> FulfilOrderAsync(int orderId, CancellationToken ct = default)
    {
        var order = await LoadOrderAsync(orderId, ct);

        if (order.Status == OrderStatus.Fulfilled)
        {
            return order.Payment!;
        }

        if (order.Status != OrderStatus.PaymentAuthorized)
        {
            throw new InvalidOrderStateException($"Order {orderId} is {order.Status} and cannot be fulfilled.");
        }

        var payment = order.Payment!;
        var authorizationId = payment.AuthorizationId;

        if (payment.AuthorizationExpiresAt.HasValue && payment.AuthorizationExpiresAt.Value <= DateTimeOffset.UtcNow)
        {
            authorizationId = await RenewAuthorizationAsync(payment, ct);
        }

        CaptureResult captureResult;
        try
        {
            captureResult = await _gateway.CaptureAsync(authorizationId, $"capture-auth-{authorizationId}", ct);
        }
        catch (AuthorizationExpiredException)
        {
            authorizationId = await RenewAuthorizationAsync(payment, ct);
            captureResult = await _gateway.CaptureAsync(authorizationId, $"capture-auth-{authorizationId}", ct);
        }

        payment.RecordCapture(captureResult.CaptureId, captureResult.Status, captureResult.CapturedAmount, captureResult.PayPalFeeAmount, captureResult.NetAmount, captureResult.CapturedAt);
        order.MarkFulfilled();

        await _orderRepository.UpdateAsync(order, ct);
        return payment;
    }

    private async Task<string> RenewAuthorizationAsync(Payment payment, CancellationToken ct)
    {
        var reauth = await _gateway.ReauthorizeAsync(payment.AuthorizationId, ct);
        payment.RecordReauthorization(reauth.AuthorizationId, reauth.Status, reauth.ExpiresAt);
        return reauth.AuthorizationId;
    }

    public async Task<Payment> CancelOrderAsync(int orderId, CancellationToken ct = default)
    {
        var order = await LoadOrderAsync(orderId, ct);

        if (order.Status == OrderStatus.Cancelled)
        {
            return order.Payment!;
        }

        if (order.Status != OrderStatus.PaymentAuthorized)
        {
            throw new InvalidOrderStateException($"Order {orderId} is {order.Status} and cannot be cancelled.");
        }

        var payment = order.Payment!;
        await _gateway.VoidAsync(payment.AuthorizationId, ct);
        payment.RecordVoid();
        order.MarkCancelled();

        await _orderRepository.UpdateAsync(order, ct);
        return payment;
    }

    public async Task<Refund> RefundOrderAsync(int orderId, string buyerId, decimal? amount, string idempotencyKey, CancellationToken ct = default)
    {
        Guard.Against.NullOrEmpty(idempotencyKey, nameof(idempotencyKey));

        var order = await LoadOwnedOrderAsync(orderId, buyerId, ct);
        if (order.Status != OrderStatus.Fulfilled)
        {
            throw new InvalidOrderStateException($"Order {orderId} is {order.Status} and has nothing captured to refund.");
        }

        var payment = order.Payment!;

        var existing = payment.Refunds.FirstOrDefault(r => r.IdempotencyKey == idempotencyKey);
        if (existing is not null)
        {
            return existing;
        }

        var refundableBalance = (payment.CapturedAmount ?? 0m) - payment.RefundedAmount;
        var refundAmount = amount ?? refundableBalance;

        if (refundAmount <= 0 || refundAmount > refundableBalance)
        {
            throw new InvalidOrderStateException($"Requested refund {refundAmount} exceeds the refundable balance {refundableBalance} for order {orderId}.");
        }

        var result = await _gateway.RefundAsync(payment.CaptureId!, refundAmount, payment.Currency, idempotencyKey, ct);
        var refund = payment.AddRefund(result.RefundId, refundAmount, RefundStatus.Completed, idempotencyKey, DateTimeOffset.UtcNow);

        await _orderRepository.UpdateAsync(order, ct);
        return refund;
    }

    public async Task<ReconciliationReport> ReconcileAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken ct = default)
    {
        var transactions = await _gateway.SearchTransactionsAsync(from, to, ct);
        var localOrders = await _orderRepository.ListAsync(new OrdersCapturedInRangeSpec(from, to), ct);

        var reconciledLocal = localOrders.Select(o => new ReconciledOrder(o.Id, o.Payment!.CaptureId, o.Payment.CapturedAmount, o.Payment.CaptureStatus, o.Payment.CapturedAt)).ToList();
        var localCaptureIds = reconciledLocal.Where(r => r.CaptureId is not null).Select(r => r.CaptureId).ToHashSet();
        var payPalTransactionIds = transactions.Where(t => t.TransactionId is not null).Select(t => t.TransactionId).ToHashSet();

        var matched = reconciledLocal.Where(r => r.CaptureId is not null && payPalTransactionIds.Contains(r.CaptureId)).ToList();
        var unmatchedLocal = reconciledLocal.Where(r => r.CaptureId is null || !payPalTransactionIds.Contains(r.CaptureId)).ToList();
        var unmatchedPayPal = transactions.Where(t => t.TransactionId is null || !localCaptureIds.Contains(t.TransactionId)).ToList();

        return new ReconciliationReport(from, to, transactions, matched, unmatchedPayPal, unmatchedLocal);
    }

    private async Task<Order> LoadOrderAsync(int orderId, CancellationToken ct)
    {
        var order = await _orderRepository.FirstOrDefaultAsync(new OrderWithPaymentByIdSpec(orderId), ct);
        if (order is null)
        {
            throw new OrderNotFoundException(orderId);
        }

        return order;
    }

    private async Task<Order> LoadOwnedOrderAsync(int orderId, string buyerId, CancellationToken ct)
    {
        var order = await LoadOrderAsync(orderId, ct);
        if (order.BuyerId != buyerId)
        {
            throw new OrderNotFoundException(orderId);
        }

        return order;
    }
}
