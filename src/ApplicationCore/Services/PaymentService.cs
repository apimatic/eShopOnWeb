using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Ardalis.GuardClauses;
using Microsoft.eShopWeb.ApplicationCore.Configuration;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Models.PayPal;
using Microsoft.eShopWeb.ApplicationCore.Specifications;

namespace Microsoft.eShopWeb.ApplicationCore.Services;

public class PaymentService : IPaymentService
{
    private readonly IRepository<Order> _orderRepository;
    private readonly IRepository<Payment> _paymentRepository;
    private readonly IRepository<SavedPaymentMethod> _savedPaymentMethodRepository;
    private readonly IPayPalClient _payPalClient;
    private readonly PayPalSettings _settings;

    public PaymentService(
        IRepository<Order> orderRepository,
        IRepository<Payment> paymentRepository,
        IRepository<SavedPaymentMethod> savedPaymentMethodRepository,
        IPayPalClient payPalClient,
        PayPalSettings settings)
    {
        _orderRepository = orderRepository;
        _paymentRepository = paymentRepository;
        _savedPaymentMethodRepository = savedPaymentMethodRepository;
        _payPalClient = payPalClient;
        _settings = settings;
    }

    public async Task<Payment?> GetPaymentForOrderAsync(int orderId, CancellationToken cancellationToken = default)
    {
        return await _paymentRepository.FirstOrDefaultAsync(new PaymentByOrderIdSpecification(orderId), cancellationToken);
    }

    public async Task<Payment> AuthorizePaymentAsync(Order order, CardDetails? card, int? savedPaymentMethodId, CancellationToken cancellationToken = default)
    {
        Guard.Against.Null(order, nameof(order));

        var existing = await GetPaymentForOrderAsync(order.Id, cancellationToken);
        if (existing != null)
        {
            // Idempotency: never authorize the same order twice.
            if (existing.Status is PaymentStatus.Authorized or PaymentStatus.Captured
                or PaymentStatus.PartiallyRefunded or PaymentStatus.Refunded)
            {
                return existing;
            }
            if (existing.Status == PaymentStatus.Voided)
            {
                throw new InvalidPaymentStateException(
                    $"Order {order.Id} has a voided payment and cannot be paid again.");
            }
            if (existing.Status == PaymentStatus.AuthorizationFailed)
            {
                // A declined attempt may be retried (e.g. with a different card).
                await _paymentRepository.DeleteAsync(existing, cancellationToken);
                existing = null;
            }
        }

        if (order.Status != OrderStatus.AwaitingPayment)
        {
            throw new InvalidPaymentStateException($"Order {order.Id} is {order.Status} and cannot be paid.");
        }

        string? vaultTokenId = null;
        if (savedPaymentMethodId.HasValue)
        {
            var savedMethods = await _savedPaymentMethodRepository.ListAsync(
                new SavedPaymentMethodsByBuyerIdSpecification(order.BuyerId), cancellationToken);
            var saved = savedMethods.FirstOrDefault(m => m.Id == savedPaymentMethodId.Value);
            if (saved == null)
            {
                throw new InvalidPaymentStateException($"Saved payment method {savedPaymentMethodId} was not found for this shopper.");
            }
            vaultTokenId = saved.VaultTokenId;
        }
        else if (card == null)
        {
            throw new InvalidPaymentStateException("Either card details or a saved payment method id must be supplied.");
        }

        var amount = order.Total();
        var currency = _settings.Currency;

        // Resume an interrupted attempt (PendingAuthorization) instead of starting over.
        var payment = existing ?? new Payment(order.Id, order.BuyerId, amount, currency);

        string payPalOrderId;
        if (payment.PayPalOrderId != null)
        {
            payPalOrderId = payment.PayPalOrderId;
        }
        else
        {
            payPalOrderId = await _payPalClient.CreateOrderAsync(
                amount, currency, $"eshop-order-{order.Id}", $"eshop-order-{order.Id}-create", cancellationToken);
            payment.SetPayPalOrderId(payPalOrderId);
        }

        try
        {
            var authorization = vaultTokenId != null
                ? await _payPalClient.AuthorizeOrderWithVaultAsync(payPalOrderId, vaultTokenId, $"eshop-order-{order.Id}-authorize", cancellationToken)
                : await _payPalClient.AuthorizeOrderWithCardAsync(payPalOrderId, card!, $"eshop-order-{order.Id}-authorize", cancellationToken);

            payment.MarkAuthorized(authorization.AuthorizationId, authorization.Status, authorization.ExpiresAt, vaultTokenId);
            order.MarkPaid();
        }
        catch (PayPalApiException ex)
        {
            payment.MarkAuthorizationFailed("FAILED");
            if (existing == null)
            {
                await _paymentRepository.AddAsync(payment, cancellationToken);
            }
            else
            {
                await _paymentRepository.UpdateAsync(payment, cancellationToken);
            }
            throw new PaymentDeclinedException(
                $"PayPal could not authorize the payment for order {order.Id}: {ex.Message}", ex);
        }

        if (existing == null)
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

    public async Task<Payment> CapturePaymentAsync(Order order, CancellationToken cancellationToken = default)
    {
        Guard.Against.Null(order, nameof(order));

        var payment = await GetPaymentForOrderAsync(order.Id, cancellationToken);
        if (payment == null)
        {
            throw new InvalidPaymentStateException($"Order {order.Id} has no payment; it must be paid before it can be fulfilled.");
        }

        // Idempotency: fulfilling twice never captures twice.
        if (payment.Status is PaymentStatus.Captured or PaymentStatus.PartiallyRefunded or PaymentStatus.Refunded)
        {
            return payment;
        }

        if (payment.Status != PaymentStatus.Authorized || payment.AuthorizationId == null)
        {
            throw new InvalidPaymentStateException(
                $"Order {order.Id} has a payment in status {payment.Status} and cannot be fulfilled.");
        }

        PayPalAuthorizationResult? authorization = null;
        try
        {
            authorization = await _payPalClient.GetAuthorizationAsync(payment.AuthorizationId, cancellationToken);
        }
        catch (PayPalApiException)
        {
            // Authorization details unavailable; treat as stale and try to renew below.
        }

        if (authorization == null || !string.Equals(authorization.Status, "CREATED", System.StringComparison.OrdinalIgnoreCase))
        {
            // The hold has gone stale; renew it instead of failing the fulfilment.
            try
            {
                authorization = await _payPalClient.ReauthorizeAsync(
                    payment.AuthorizationId, payment.Amount, payment.Currency,
                    $"eshop-order-{order.Id}-reauthorize", cancellationToken);
                payment.UpdateAuthorization(authorization.AuthorizationId, authorization.Status, authorization.ExpiresAt);
            }
            catch (PayPalApiException ex)
            {
                throw new AuthorizationRenewalException(
                    $"The PayPal authorization for order {order.Id} has expired and could not be renewed ({ex.Message}). " +
                    "Ask the shopper to pay again, or cancel the order.", ex);
            }
        }

        var capture = await _payPalClient.CaptureAuthorizationAsync(
            payment.AuthorizationId, $"eshop-order-{order.Id}-capture", cancellationToken);

        payment.MarkCaptured(capture.CaptureId, capture.Amount, capture.PayPalFee, capture.NetAmount);
        order.MarkFulfilled();

        await _paymentRepository.UpdateAsync(payment, cancellationToken);
        await _orderRepository.UpdateAsync(order, cancellationToken);
        return payment;
    }

    public async Task<Payment?> VoidPaymentAsync(Order order, CancellationToken cancellationToken = default)
    {
        Guard.Against.Null(order, nameof(order));

        if (order.Status == OrderStatus.Fulfilled)
        {
            throw new InvalidPaymentStateException(
                $"Order {order.Id} has already been fulfilled and cannot be cancelled; issue a refund instead.");
        }

        var payment = await GetPaymentForOrderAsync(order.Id, cancellationToken);

        if (payment != null)
        {
            if (payment.Status == PaymentStatus.Authorized && payment.AuthorizationId != null)
            {
                await _payPalClient.VoidAuthorizationAsync(
                    payment.AuthorizationId, $"eshop-order-{order.Id}-void", cancellationToken);
                payment.MarkVoided();
                await _paymentRepository.UpdateAsync(payment, cancellationToken);
            }
            else if (payment.Status != PaymentStatus.Voided)
            {
                throw new InvalidPaymentStateException(
                    $"Order {order.Id} has a payment in status {payment.Status} and cannot be cancelled.");
            }
            // Already voided: fall through as an idempotent no-op.
        }

        if (order.Status != OrderStatus.Cancelled)
        {
            order.MarkCancelled();
            await _orderRepository.UpdateAsync(order, cancellationToken);
        }

        return payment;
    }

    public async Task<PaymentRefund> RefundPaymentAsync(Order order, decimal? amount, string idempotencyKey, CancellationToken cancellationToken = default)
    {
        Guard.Against.Null(order, nameof(order));
        Guard.Against.NullOrEmpty(idempotencyKey, nameof(idempotencyKey));

        var payment = await GetPaymentForOrderAsync(order.Id, cancellationToken);
        if (payment == null || payment.Status is not (PaymentStatus.Captured or PaymentStatus.PartiallyRefunded))
        {
            throw new InvalidPaymentStateException(
                $"Order {order.Id} has no captured payment to refund (payment status: {payment?.Status.ToString() ?? "none"}).");
        }

        // Idempotency: a repeated key returns the original refund instead of refunding twice.
        var existingRefund = payment.Refunds.FirstOrDefault(r => r.IdempotencyKey == idempotencyKey);
        if (existingRefund != null)
        {
            return existingRefund;
        }

        var refundAmount = amount ?? payment.RefundableAmount;
        if (refundAmount <= 0m || refundAmount > payment.RefundableAmount)
        {
            throw new InvalidPaymentStateException(
                $"Refund amount {refundAmount} exceeds the refundable amount {payment.RefundableAmount} for order {order.Id}.");
        }

        var requestId = $"eshop-refund-{idempotencyKey}";
        if (requestId.Length > 108)
        {
            requestId = requestId.Substring(0, 108);
        }

        var result = await _payPalClient.RefundCaptureAsync(
            payment.CaptureId!, refundAmount, payment.Currency, requestId, cancellationToken);

        var refund = payment.AddRefund(result.RefundId, refundAmount, idempotencyKey, result.Status);
        await _paymentRepository.UpdateAsync(payment, cancellationToken);
        return refund;
    }
}
