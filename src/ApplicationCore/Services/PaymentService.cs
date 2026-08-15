using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.VaultAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Payments;
using Microsoft.eShopWeb.ApplicationCore.Specifications;

namespace Microsoft.eShopWeb.ApplicationCore.Services;

/// <summary>
/// Drives authorize / fulfil / cancel / refund against the payment gateway and keeps the order and
/// <see cref="Payment"/> aggregates in step. A per-order lock serializes concurrent requests for the
/// same order so a double-click never authorizes or captures twice; every gateway write also carries
/// a deterministic idempotency key so PayPal itself de-duplicates.
/// </summary>
public class PaymentService : IPaymentService
{
    private static readonly ConcurrentDictionary<int, SemaphoreSlim> OrderLocks = new();

    // A per-process nonce keeps the PayPal-Request-Id values we send unique across runs (the sandbox
    // account remembers keys), while remaining stable within a run so a double-click still de-duplicates.
    private static readonly string ProcessNonce = Guid.NewGuid().ToString("N").Substring(0, 12);

    private readonly IRepository<Order> _orderRepository;
    private readonly IRepository<Payment> _paymentRepository;
    private readonly IReadRepository<VaultedCard> _vaultRepository;
    private readonly IPaymentGateway _gateway;
    private readonly IPaymentConfiguration _configuration;

    public PaymentService(
        IRepository<Order> orderRepository,
        IRepository<Payment> paymentRepository,
        IReadRepository<VaultedCard> vaultRepository,
        IPaymentGateway gateway,
        IPaymentConfiguration configuration)
    {
        _orderRepository = orderRepository;
        _paymentRepository = paymentRepository;
        _vaultRepository = vaultRepository;
        _gateway = gateway;
        _configuration = configuration;
    }

    public async Task<OrderPaymentState> AuthorizeAsync(int orderId, string buyerId, PaymentInstruction instruction, CancellationToken cancellationToken = default)
    {
        if (!instruction.IsValid)
        {
            throw new OrderRequestInvalidException("Provide exactly one of: card details, or a saved card id.");
        }

        var gate = await AcquireAsync(orderId, cancellationToken);
        try
        {
            var order = await LoadOwnedOrderAsync(orderId, buyerId);
            var existing = await _paymentRepository.FirstOrDefaultAsync(new PaymentByOrderIdSpecification(orderId), cancellationToken);

            if (order.Status != OrderStatus.AwaitingPayment)
            {
                // Idempotent: a successful prior authorization returns the same payment on re-submit.
                if (existing is not null && existing.Status != PaymentStatus.Failed && existing.Status != PaymentStatus.Voided)
                {
                    return new OrderPaymentState(order, existing);
                }
                throw new PaymentDomainException($"Order {orderId} is not awaiting payment (status {order.Status}).");
            }

            // Order is awaiting payment. Clear any stale non-successful payment record before retrying.
            if (existing is not null)
            {
                await _paymentRepository.DeleteAsync(existing, cancellationToken);
            }

            var currency = _configuration.Currency;
            var amount = order.Total();
            var idempotencyKey = $"auth-{ProcessNonce}-order-{orderId}";

            var authResult = instruction.SavedCardId.HasValue
                ? await AuthorizeWithSavedCardAsync(instruction.SavedCardId.Value, buyerId, amount, currency, idempotencyKey, cancellationToken)
                : await _gateway.AuthorizeWithCardAsync(amount, currency, instruction.Card!, idempotencyKey, cancellationToken);

            var payment = new Payment(orderId, buyerId, currency, amount, authResult.PayPalOrderId);
            payment.SetAuthorized(authResult.AuthorizationId, authResult.Status);
            await _paymentRepository.AddAsync(payment, cancellationToken);

            order.MarkAuthorized();
            await _orderRepository.UpdateAsync(order, cancellationToken);

            return new OrderPaymentState(order, payment);
        }
        finally
        {
            Release(orderId, gate);
        }
    }

    private async Task<Payments.AuthorizationResult> AuthorizeWithSavedCardAsync(
        int savedCardId, string buyerId, decimal amount, string currency, string idempotencyKey, CancellationToken cancellationToken)
    {
        var card = await _vaultRepository.FirstOrDefaultAsync(new VaultedCardByIdSpecification(savedCardId, buyerId), cancellationToken);
        if (card is null)
        {
            throw new NotFoundException($"Saved card {savedCardId} was not found.");
        }
        return await _gateway.AuthorizeWithVaultedCardAsync(amount, currency, card.VaultId, idempotencyKey, cancellationToken);
    }

    public async Task<OrderPaymentState> FulfilAsync(int orderId, CancellationToken cancellationToken = default)
    {
        var gate = await AcquireAsync(orderId, cancellationToken);
        try
        {
            var order = await LoadOrderAsync(orderId);
            var payment = await _paymentRepository.FirstOrDefaultAsync(new PaymentByOrderIdSpecification(orderId), cancellationToken);

            if (order.Status == OrderStatus.Fulfilled)
            {
                return new OrderPaymentState(order, payment); // already captured — idempotent
            }
            if (order.Status != OrderStatus.PaymentAuthorized || payment is null || payment.AuthorizationId is null)
            {
                throw new PaymentDomainException($"Order {orderId} cannot be fulfilled (status {order.Status}); it must be authorized first.");
            }

            var amount = order.Total();
            var currency = payment.Currency;
            var captureKey = $"capture-{ProcessNonce}-order-{orderId}";

            Payments.CaptureResult capture;
            try
            {
                capture = await _gateway.CaptureAuthorizationAsync(payment.AuthorizationId, amount, currency, captureKey, cancellationToken);
            }
            catch (AuthorizationExpiredException)
            {
                // The hold went stale before fulfilment — renew it, then capture the renewed authorization.
                // ReauthorizeAsync throws ReauthorizationNotPossibleException (operator-actionable) if it cannot.
                var renewed = await _gateway.ReauthorizeAsync(payment.AuthorizationId, amount, currency, cancellationToken);
                payment.RenewAuthorization(renewed.AuthorizationId, renewed.Status);
                await _paymentRepository.UpdateAsync(payment, cancellationToken);

                capture = await _gateway.CaptureAuthorizationAsync(payment.AuthorizationId, amount, currency, captureKey, cancellationToken);
            }

            payment.SetCaptured(capture.CaptureId, capture.Status, capture.Amount, capture.PayPalFee, capture.NetAmount);
            await _paymentRepository.UpdateAsync(payment, cancellationToken);

            order.MarkFulfilled();
            await _orderRepository.UpdateAsync(order, cancellationToken);

            return new OrderPaymentState(order, payment);
        }
        finally
        {
            Release(orderId, gate);
        }
    }

    public async Task<OrderPaymentState> CancelAsync(int orderId, CancellationToken cancellationToken = default)
    {
        var gate = await AcquireAsync(orderId, cancellationToken);
        try
        {
            var order = await LoadOrderAsync(orderId);
            var payment = await _paymentRepository.FirstOrDefaultAsync(new PaymentByOrderIdSpecification(orderId), cancellationToken);

            if (order.Status == OrderStatus.Cancelled)
            {
                return new OrderPaymentState(order, payment); // idempotent
            }

            if (order.Status == OrderStatus.PaymentAuthorized && payment is { Status: PaymentStatus.Authorized, AuthorizationId: not null })
            {
                await _gateway.VoidAuthorizationAsync(payment.AuthorizationId, cancellationToken);
                payment.Void();
                await _paymentRepository.UpdateAsync(payment, cancellationToken);
            }
            else if (order.Status != OrderStatus.AwaitingPayment)
            {
                throw new PaymentDomainException($"Order {orderId} cannot be cancelled (status {order.Status}); cancel applies only before fulfilment.");
            }

            order.MarkCancelled();
            await _orderRepository.UpdateAsync(order, cancellationToken);

            return new OrderPaymentState(order, payment);
        }
        finally
        {
            Release(orderId, gate);
        }
    }

    public async Task<PaymentRefund> RefundAsync(int orderId, string buyerId, decimal? amount, string idempotencyKey, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(idempotencyKey))
        {
            throw new OrderRequestInvalidException("A refund idempotency key is required.");
        }

        var gate = await AcquireAsync(orderId, cancellationToken);
        try
        {
            var order = await LoadOwnedOrderAsync(orderId, buyerId);
            var payment = await _paymentRepository.FirstOrDefaultAsync(new PaymentByOrderIdSpecification(orderId), cancellationToken);

            if (payment is null || payment.CaptureId is null)
            {
                throw new PaymentDomainException($"Order {orderId} has no captured payment to refund.");
            }

            // Idempotency: a repeat under the same key returns the existing refund rather than refunding twice.
            var priorRefund = payment.FindRefundByIdempotencyKey(idempotencyKey);
            if (priorRefund is not null)
            {
                return priorRefund;
            }

            var refundAmount = amount ?? payment.RefundableRemaining();
            payment.EnsureRefundable(refundAmount); // enforces: never refundable beyond what was captured

            // My layer already de-duplicates a repeated caller key above; the value sent to PayPal is
            // namespaced to this capture and process so it never collides with an unrelated prior use.
            var payPalRequestId = $"refund-{ProcessNonce}-{payment.CaptureId}-{idempotencyKey}";
            var result = await _gateway.RefundCaptureAsync(payment.CaptureId, refundAmount, payment.Currency, payPalRequestId, cancellationToken);
            var refund = new PaymentRefund(idempotencyKey, result.RefundId, result.Amount, result.Currency, result.Status);
            payment.AddRefund(refund);
            await _paymentRepository.UpdateAsync(payment, cancellationToken);

            order.MarkRefunded(fullyRefunded: payment.RefundableRemaining() <= 0m);
            await _orderRepository.UpdateAsync(order, cancellationToken);

            return refund;
        }
        finally
        {
            Release(orderId, gate);
        }
    }

    public async Task<OrderPaymentState?> GetForBuyerAsync(int orderId, string buyerId, CancellationToken cancellationToken = default)
    {
        var order = await _orderRepository.FirstOrDefaultAsync(new OrderWithItemsByIdSpec(orderId), cancellationToken);
        if (order is null || !string.Equals(order.BuyerId, buyerId, StringComparison.Ordinal))
        {
            return null;
        }
        var payment = await _paymentRepository.FirstOrDefaultAsync(new PaymentByOrderIdSpecification(orderId), cancellationToken);
        return new OrderPaymentState(order, payment);
    }

    public async Task<IReadOnlyList<OrderPaymentState>> ListForBuyerAsync(string buyerId, CancellationToken cancellationToken = default)
    {
        var orders = await _orderRepository.ListAsync(new CustomerOrdersWithItemsSpecification(buyerId), cancellationToken);
        if (orders.Count == 0)
        {
            return new List<OrderPaymentState>();
        }

        var orderIds = orders.Select(o => o.Id).ToList();
        var payments = await _paymentRepository.ListAsync(new PaymentsByOrderIdsSpecification(orderIds), cancellationToken);
        var paymentByOrder = payments
            .GroupBy(p => p.OrderId)
            .ToDictionary(g => g.Key, g => g.First());

        return orders
            .Select(o => new OrderPaymentState(o, paymentByOrder.TryGetValue(o.Id, out var p) ? p : null))
            .ToList();
    }

    // ---- helpers ----

    private async Task<Order> LoadOrderAsync(int orderId)
    {
        var order = await _orderRepository.FirstOrDefaultAsync(new OrderWithItemsByIdSpec(orderId));
        if (order is null)
        {
            throw new NotFoundException($"Order {orderId} was not found.");
        }
        return order;
    }

    private async Task<Order> LoadOwnedOrderAsync(int orderId, string buyerId)
    {
        var order = await LoadOrderAsync(orderId);
        if (!string.Equals(order.BuyerId, buyerId, StringComparison.Ordinal))
        {
            // Do not reveal that the order exists for another buyer.
            throw new NotFoundException($"Order {orderId} was not found.");
        }
        return order;
    }

    private static async Task<SemaphoreSlim> AcquireAsync(int orderId, CancellationToken cancellationToken)
    {
        var gate = OrderLocks.GetOrAdd(orderId, _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(cancellationToken);
        return gate;
    }

    private static void Release(int orderId, SemaphoreSlim gate)
    {
        gate.Release();
    }
}
