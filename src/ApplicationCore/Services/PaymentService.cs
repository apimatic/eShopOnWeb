using System;
using System.Collections.Concurrent;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Ardalis.GuardClauses;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Models.Payments;
using Microsoft.eShopWeb.ApplicationCore.Specifications;

namespace Microsoft.eShopWeb.ApplicationCore.Services;

public class PaymentService : IPaymentService
{
    // Serializes payment operations per order so a double-click cannot race itself
    // into authorizing or capturing twice.
    private static readonly ConcurrentDictionary<int, SemaphoreSlim> OrderLocks = new();

    private readonly IRepository<Order> _orderRepository;
    private readonly IRepository<Payment> _paymentRepository;
    private readonly IRepository<SavedCard> _savedCardRepository;
    private readonly IPaymentGateway _paymentGateway;
    private readonly string _currency;

    public PaymentService(IRepository<Order> orderRepository,
        IRepository<Payment> paymentRepository,
        IRepository<SavedCard> savedCardRepository,
        IPaymentGateway paymentGateway,
        IPaymentCurrencyProvider currencyProvider)
    {
        _orderRepository = orderRepository;
        _paymentRepository = paymentRepository;
        _savedCardRepository = savedCardRepository;
        _paymentGateway = paymentGateway;
        _currency = currencyProvider.Currency;
    }

    public async Task<Payment> AuthorizeOrderPaymentAsync(string buyerId, int orderId, CardDetails? card, int? savedCardId,
        CancellationToken cancellationToken = default)
    {
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));
        if (card is null && savedCardId is null)
        {
            throw new PaymentOperationException("Provide either card details or a saved card to pay with.");
        }

        var orderLock = OrderLocks.GetOrAdd(orderId, _ => new SemaphoreSlim(1, 1));
        await orderLock.WaitAsync(cancellationToken);
        try
        {
            var order = await GetOwnOrderAsync(buyerId, orderId, cancellationToken);
            var payment = await GetPaymentForOrderAsync(orderId, cancellationToken);

            // Idempotent replay: the hold already exists, just report it.
            if (payment is not null && payment.Status == PaymentStatus.Authorized)
            {
                return payment;
            }
            if (payment is not null && payment.Status is PaymentStatus.Captured or PaymentStatus.PartiallyRefunded or PaymentStatus.Refunded)
            {
                throw new PaymentOperationException($"Order {orderId} has already been captured; it cannot be paid again.");
            }
            if (payment is not null && payment.Status == PaymentStatus.Voided)
            {
                throw new PaymentOperationException($"Order {orderId} was cancelled and its authorization voided; it cannot be paid.");
            }

            string? vaultTokenId = null;
            if (savedCardId is not null)
            {
                var savedCard = await _savedCardRepository.FirstOrDefaultAsync(
                    new SavedCardByIdAndBuyerSpec(savedCardId.Value, buyerId), cancellationToken);
                if (savedCard is null)
                {
                    throw new PaymentOperationException($"Saved card {savedCardId} was not found for this shopper.");
                }
                vaultTokenId = savedCard.VaultTokenId;
            }

            payment ??= new Payment(order.Id, buyerId, order.Total(), _currency);

            var authorizeRequestId = $"eshop-auth-{order.Id}-{Guid.NewGuid():N}";
            var result = await _paymentGateway.AuthorizeOrderAsync(
                new AuthorizationRequest(
                    Amount: order.Total(),
                    Currency: _currency,
                    CustomId: order.Id.ToString(),
                    // PayPal enforces invoice-id uniqueness per merchant account, so each
                    // authorization attempt gets its own suffix.
                    InvoiceId: $"eshop-order-{order.Id}-{Guid.NewGuid():N}",
                    IdempotencyKey: authorizeRequestId,
                    Card: card,
                    VaultTokenId: vaultTokenId),
                cancellationToken);

            payment.MarkAuthorized(result.PayPalOrderId, result.AuthorizationId, result.Status,
                result.Amount, result.ExpirationTime, authorizeRequestId);
            order.MarkAuthorized();

            if (payment.Id == 0)
            {
                await _paymentRepository.AddAsync(payment, cancellationToken);
            }
            else
            {
                await _paymentRepository.UpdateAsync(payment, cancellationToken);
            }
            await _orderRepository.UpdateAsync(order, cancellationToken);

            return payment;
        }
        finally
        {
            orderLock.Release();
        }
    }

    public async Task<Payment> FulfilOrderAsync(int orderId, CancellationToken cancellationToken = default)
    {
        var orderLock = OrderLocks.GetOrAdd(orderId, _ => new SemaphoreSlim(1, 1));
        await orderLock.WaitAsync(cancellationToken);
        try
        {
            var order = await _orderRepository.FirstOrDefaultAsync(new OrderWithItemsByIdSpec(orderId), cancellationToken);
            if (order is null)
            {
                throw new PaymentOperationException($"Order {orderId} was not found.");
            }
            var payment = await GetPaymentForOrderAsync(orderId, cancellationToken);

            // Idempotent replay: already captured, report the capture PayPal gave us.
            if (order.Status == OrderStatus.Fulfilled && payment is not null)
            {
                return payment;
            }
            if (order.Status != OrderStatus.Authorized || payment is null || payment.AuthorizationId is null)
            {
                throw new PaymentOperationException($"Order {orderId} is in status {order.Status} and cannot be fulfilled; it must be paid first.");
            }

            await EnsureAuthorizationCapturableAsync(payment, cancellationToken);

            var captureRequestId = payment.CaptureRequestId ?? $"eshop-capture-{order.Id}-{Guid.NewGuid():N}";
            // No invoice id on the capture: PayPal then reports the authorization's invoice id.
            var capture = await _paymentGateway.CaptureAuthorizationAsync(
                payment.AuthorizationId, payment.AuthorizedAmount, payment.Currency,
                invoiceId: null, captureRequestId, cancellationToken);

            if (!string.Equals(capture.Status, "COMPLETED", StringComparison.OrdinalIgnoreCase))
            {
                throw new PaymentGatewayException(System.Net.HttpStatusCode.UnprocessableEntity, null,
                    $"PayPal did not complete the capture for order {order.Id}; status reported: {capture.Status}. Retry the fulfilment or investigate in the PayPal dashboard.", null);
            }

            payment.MarkCaptured(capture.CaptureId, capture.Status, capture.Amount, capture.PayPalFee, capture.NetAmount, captureRequestId);
            order.MarkFulfilled();

            await _paymentRepository.UpdateAsync(payment, cancellationToken);
            await _orderRepository.UpdateAsync(order, cancellationToken);

            return payment;
        }
        finally
        {
            orderLock.Release();
        }
    }

    public async Task<Payment?> CancelOrderAsync(int orderId, CancellationToken cancellationToken = default)
    {
        var orderLock = OrderLocks.GetOrAdd(orderId, _ => new SemaphoreSlim(1, 1));
        await orderLock.WaitAsync(cancellationToken);
        try
        {
            var order = await _orderRepository.FirstOrDefaultAsync(new OrderWithItemsByIdSpec(orderId), cancellationToken);
            if (order is null)
            {
                throw new PaymentOperationException($"Order {orderId} was not found.");
            }
            var payment = await GetPaymentForOrderAsync(orderId, cancellationToken);

            if (order.Status == OrderStatus.Cancelled)
            {
                return payment; // idempotent replay
            }

            order.MarkCancelled();

            if (payment is not null && payment.Status == PaymentStatus.Authorized && payment.AuthorizationId is not null)
            {
                await _paymentGateway.VoidAuthorizationAsync(
                    payment.AuthorizationId, $"eshop-void-{order.Id}-{payment.AuthorizationId}", cancellationToken);
                payment.MarkVoided();
                await _paymentRepository.UpdateAsync(payment, cancellationToken);
            }

            await _orderRepository.UpdateAsync(order, cancellationToken);
            return payment;
        }
        finally
        {
            orderLock.Release();
        }
    }

    public async Task<PaymentRefund> RefundOrderAsync(string buyerId, int orderId, decimal? amount, string idempotencyKey,
        string? noteToPayer, CancellationToken cancellationToken = default)
    {
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));
        Guard.Against.NullOrEmpty(idempotencyKey, nameof(idempotencyKey));

        var orderLock = OrderLocks.GetOrAdd(orderId, _ => new SemaphoreSlim(1, 1));
        await orderLock.WaitAsync(cancellationToken);
        try
        {
            var order = await GetOwnOrderAsync(buyerId, orderId, cancellationToken);
            var payment = await GetPaymentForOrderAsync(orderId, cancellationToken);

            // Idempotent replay under the caller-supplied key.
            var existing = payment?.Refunds.FirstOrDefault(r => r.IdempotencyKey == idempotencyKey);
            if (existing is not null)
            {
                return existing;
            }

            if (order.Status != OrderStatus.Fulfilled || payment is null || payment.CaptureId is null)
            {
                throw new PaymentOperationException($"Order {orderId} has no captured payment to refund; only fulfilled orders can be refunded.");
            }

            var refundAmount = amount ?? payment.RefundableAmount;
            if (refundAmount <= 0m || refundAmount > payment.RefundableAmount)
            {
                throw new PaymentOperationException(
                    $"Refund of {refundAmount:F2} {payment.Currency} exceeds the remaining refundable amount of {payment.RefundableAmount:F2} {payment.Currency} on order {orderId}.");
            }

            var result = await _paymentGateway.RefundCaptureAsync(
                payment.CaptureId, refundAmount, payment.Currency, idempotencyKey, noteToPayer, cancellationToken);

            var refund = payment.AddRefund(result.RefundId, result.Amount, result.Status, idempotencyKey);
            await _paymentRepository.UpdateAsync(payment, cancellationToken);

            return refund;
        }
        finally
        {
            orderLock.Release();
        }
    }

    public async Task<Payment?> GetPaymentForOrderAsync(int orderId, CancellationToken cancellationToken = default)
    {
        return await _paymentRepository.FirstOrDefaultAsync(new PaymentByOrderIdSpec(orderId), cancellationToken);
    }

    private async Task EnsureAuthorizationCapturableAsync(Payment payment, CancellationToken cancellationToken)
    {
        var authorization = await _paymentGateway.GetAuthorizationAsync(payment.AuthorizationId!, cancellationToken);

        var expired = authorization.ExpirationTime is not null && authorization.ExpirationTime <= DateTimeOffset.UtcNow;
        var capturable = authorization.Status is "CREATED" or "PENDING" && !expired;
        if (capturable)
        {
            return;
        }

        if (authorization.Status is "VOIDED" or "DENIED" or "CAPTURED" or "PARTIALLY_CAPTURED")
        {
            throw new PaymentOperationException(
                $"The authorization for order {payment.OrderId} is {authorization.Status} at PayPal and cannot be captured. Cancel the order or investigate in the PayPal dashboard.");
        }

        // The hold has gone stale: renew it rather than failing the fulfilment.
        try
        {
            var renewed = await _paymentGateway.ReauthorizeAsync(
                payment.AuthorizationId!, payment.AuthorizedAmount, payment.Currency,
                $"eshop-reauth-{payment.OrderId}-{Guid.NewGuid():N}", cancellationToken);
            payment.MarkAuthorized(payment.PayPalOrderId!, renewed.AuthorizationId, renewed.Status,
                renewed.Amount, renewed.ExpirationTime, payment.AuthorizeRequestId!);
            await _paymentRepository.UpdateAsync(payment, cancellationToken);
        }
        catch (PaymentGatewayException ex)
        {
            payment.MarkAuthorizationExpired();
            await _paymentRepository.UpdateAsync(payment, cancellationToken);
            throw new AuthorizationNotRenewableException(
                $"The authorization for order {payment.OrderId} expired and PayPal refused to renew it " +
                $"({ex.ErrorName ?? ex.Message}). PayPal only allows reauthorization within 29 days of the original hold. " +
                $"The order has been moved back to AwaitingPayment: ask the shopper to pay again, then fulfil.");
        }
    }

    private async Task<Order> GetOwnOrderAsync(string buyerId, int orderId, CancellationToken cancellationToken)
    {
        var order = await _orderRepository.FirstOrDefaultAsync(new OrderWithItemsByIdSpec(orderId), cancellationToken);
        if (order is null || !string.Equals(order.BuyerId, buyerId, StringComparison.OrdinalIgnoreCase))
        {
            throw new PaymentOperationException($"Order {orderId} was not found.");
        }
        return order;
    }
}
