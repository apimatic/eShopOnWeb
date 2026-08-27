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
using Microsoft.eShopWeb.ApplicationCore.Specifications;

namespace Microsoft.eShopWeb.ApplicationCore.Services;

public class PaymentService : IPaymentService
{
    // PayPal's honor period: an authorization is guaranteed capturable for 3 days.
    private static readonly TimeSpan HonorPeriod = TimeSpan.FromDays(3);

    private readonly IRepository<Order> _orderRepository;
    private readonly IRepository<Payment> _paymentRepository;
    private readonly IRepository<SavedPaymentMethod> _paymentMethodRepository;
    private readonly IPayPalClient _payPalClient;
    private readonly PayPalSettings _settings;
    private readonly IAppLogger<PaymentService> _logger;

    public PaymentService(
        IRepository<Order> orderRepository,
        IRepository<Payment> paymentRepository,
        IRepository<SavedPaymentMethod> paymentMethodRepository,
        IPayPalClient payPalClient,
        PayPalSettings settings,
        IAppLogger<PaymentService> logger)
    {
        _orderRepository = orderRepository;
        _paymentRepository = paymentRepository;
        _paymentMethodRepository = paymentMethodRepository;
        _payPalClient = payPalClient;
        _settings = settings;
        _logger = logger;
    }

    public async Task<Payment> AuthorizePaymentAsync(Order order, PayPalCardDetails? card,
        SavedPaymentMethod? savedPaymentMethod, CancellationToken cancellationToken = default)
    {
        Guard.Against.Null(order, nameof(order));
        if (card is null && savedPaymentMethod is null)
        {
            throw new ArgumentException("Either card details or a saved payment method must be supplied.");
        }
        if (savedPaymentMethod is not null && savedPaymentMethod.BuyerId != order.BuyerId)
        {
            throw new ArgumentException("The saved payment method does not belong to this shopper.");
        }

        var existing = await _paymentRepository.FirstOrDefaultAsync(new PaymentByOrderIdSpec(order.Id), cancellationToken);
        if (order.Status == OrderStatus.PaymentAuthorized && existing is not null)
        {
            // Idempotent retry of a pay request that already succeeded.
            return existing;
        }
        if (order.Status != OrderStatus.AwaitingPayment)
        {
            throw new InvalidOperationException($"Order {order.Id} is {order.Status} and cannot be paid.");
        }

        var total = order.Total();
        PayPalAuthorizationResult result;
        try
        {
            result = await _payPalClient.AuthorizeOrderAsync(
                referenceId: $"eshop-order-{order.Id}",
                // PayPal business accounts can require invoice ids to be unique per transaction,
                // so the invoice id is unique per authorization attempt while the reference id
                // stays tied to the order.
                invoiceId: $"eshop-order-{order.Id}-{Guid.NewGuid():N}",
                amount: total,
                currency: _settings.Currency,
                card: card,
                vaultTokenId: savedPaymentMethod?.VaultTokenId,
                // Unique per attempt: PayPal caches error responses against a request id,
                // and effect-idempotency is enforced above via the order status check.
                idempotencyKey: $"eshop-authorize-order-{order.Id}-{Guid.NewGuid():N}",
                cancellationToken: cancellationToken);
        }
        catch (PaymentGatewayException)
        {
            throw;
        }

        var payment = new Payment(order.Id, order.BuyerId, result.Currency, result.Amount,
            result.PayPalOrderId, result.AuthorizationId, result.Status, result.AuthorizedAt);
        await _paymentRepository.AddAsync(payment, cancellationToken);

        order.MarkPaymentAuthorized();
        await _orderRepository.UpdateAsync(order, cancellationToken);

        return payment;
    }

    public async Task<Payment> CapturePaymentAsync(Order order, CancellationToken cancellationToken = default)
    {
        Guard.Against.Null(order, nameof(order));
        var payment = await _paymentRepository.FirstOrDefaultAsync(new PaymentByOrderIdSpec(order.Id), cancellationToken);

        if (order.Status == OrderStatus.Fulfilled && payment is not null)
        {
            return payment;
        }
        if (order.Status != OrderStatus.PaymentAuthorized || payment is null)
        {
            throw new InvalidOperationException($"Order {order.Id} is {order.Status} and cannot be fulfilled.");
        }

        var reauthorized = false;
        if (DateTimeOffset.UtcNow - payment.AuthorizedAt > HonorPeriod)
        {
            await RenewAuthorizationAsync(payment, order, cancellationToken);
            reauthorized = true;
        }

        PayPalCaptureResult capture;
        try
        {
            capture = await _payPalClient.CaptureAuthorizationAsync(
                payment.AuthorizationId, $"eshop-capture-order-{order.Id}-{Guid.NewGuid():N}", cancellationToken);
        }
        catch (PaymentGatewayException ex) when (!reauthorized)
        {
            if (ex.PayPalIssue == "AUTHORIZATION_ALREADY_CAPTURED")
            {
                // A previous fulfil captured the money at PayPal but the result was
                // never recorded locally; recover the capture from PayPal's records.
                var captureId = await _payPalClient.GetCapturedIdForOrderAsync(payment.PayPalOrderId, cancellationToken);
                if (captureId is null)
                {
                    throw;
                }
                capture = await _payPalClient.GetCaptureAsync(captureId, cancellationToken);
            }
            else
            {
                // The hold may have gone stale on PayPal's side; renew it once and retry.
                await RenewAuthorizationAsync(payment, order, cancellationToken);
                capture = await _payPalClient.CaptureAuthorizationAsync(
                    payment.AuthorizationId, $"eshop-capture-order-{order.Id}-{Guid.NewGuid():N}", cancellationToken);
            }
        }

        payment.SetCapture(capture.CaptureId, capture.Status, capture.GrossAmount, capture.FeeAmount, capture.NetAmount);
        await _paymentRepository.UpdateAsync(payment, cancellationToken);

        order.MarkFulfilled();
        await _orderRepository.UpdateAsync(order, cancellationToken);

        return payment;
    }

    private async Task RenewAuthorizationAsync(Payment payment, Order order, CancellationToken cancellationToken)
    {
        try
        {
            var renewed = await _payPalClient.ReauthorizeAsync(
                payment.AuthorizationId, payment.AuthorizedAmount, payment.Currency,
                $"eshop-reauthorize-order-{order.Id}-{DateTimeOffset.UtcNow.Ticks}", cancellationToken);
            payment.RenewAuthorization(renewed.AuthorizationId, renewed.Status,
                renewed.CreateTime ?? DateTimeOffset.UtcNow);
            await _paymentRepository.UpdateAsync(payment, cancellationToken);
        }
        catch (PaymentGatewayException ex)
        {
            throw new AuthorizationCannotBeRenewedException(
                $"The PayPal authorization for order {order.Id} has gone stale and PayPal would not renew it " +
                $"({ex.PayPalIssue ?? ex.PayPalErrorName ?? ex.Message}). " +
                "Cancel this order and ask the shopper to place and pay for a new one.",
                ex.PayPalErrorName, ex.PayPalIssue);
        }
    }

    public async Task<Payment?> CancelPaymentAsync(Order order, CancellationToken cancellationToken = default)
    {
        Guard.Against.Null(order, nameof(order));
        if (order.Status == OrderStatus.Cancelled)
        {
            return await _paymentRepository.FirstOrDefaultAsync(new PaymentByOrderIdSpec(order.Id), cancellationToken);
        }
        if (order.Status != OrderStatus.AwaitingPayment && order.Status != OrderStatus.PaymentAuthorized)
        {
            throw new InvalidOperationException(
                $"Order {order.Id} is {order.Status} and cannot be cancelled; use a refund for fulfilled orders.");
        }

        var payment = await _paymentRepository.FirstOrDefaultAsync(new PaymentByOrderIdSpec(order.Id), cancellationToken);
        if (payment is not null && !payment.IsCaptured && payment.VoidedAt is null)
        {
            try
            {
                await _payPalClient.VoidAuthorizationAsync(payment.AuthorizationId, cancellationToken);
                payment.MarkVoided();
                await _paymentRepository.UpdateAsync(payment, cancellationToken);
            }
            catch (PaymentGatewayException ex)
            {
                // The hold could not be voided right now; it auto-expires with PayPal.
                // The order is still cancelled so no capture can happen against it.
                _logger.LogWarning($"Could not void PayPal authorization {payment.AuthorizationId}: {ex.Message}");
            }
        }

        order.MarkCancelled();
        await _orderRepository.UpdateAsync(order, cancellationToken);
        return payment;
    }

    public async Task<PaymentRefund> RefundPaymentAsync(Order order, decimal? amount,
        string idempotencyKey, CancellationToken cancellationToken = default)
    {
        Guard.Against.Null(order, nameof(order));
        Guard.Against.NullOrEmpty(idempotencyKey, nameof(idempotencyKey));

        var payment = await _paymentRepository.FirstOrDefaultAsync(new PaymentByOrderIdSpec(order.Id), cancellationToken);

        var existing = payment?.Refunds.FirstOrDefault(r => r.IdempotencyKey == idempotencyKey);
        if (existing is not null)
        {
            return existing;
        }

        if (order.Status != OrderStatus.Fulfilled && order.Status != OrderStatus.PartiallyRefunded)
        {
            throw new InvalidOperationException($"Order {order.Id} is {order.Status} and cannot be refunded.");
        }
        if (payment is null || !payment.IsCaptured)
        {
            throw new InvalidOperationException($"Order {order.Id} has no captured payment to refund.");
        }

        var refundable = payment.RefundableAmount();
        var refundAmount = amount ?? refundable;
        if (refundAmount <= 0 || refundAmount > refundable)
        {
            throw new InvalidOperationException(
                $"Refund amount {refundAmount:0.00} exceeds the refundable amount {refundable:0.00} {payment.Currency} for order {order.Id}.");
        }

        var result = await _payPalClient.RefundCaptureAsync(
            payment.CaptureId!, refundAmount, payment.Currency,
            $"eshop-refund-{idempotencyKey}", cancellationToken);

        var refund = payment.AddRefund(idempotencyKey, result.RefundId, result.Status, result.Amount);
        await _paymentRepository.UpdateAsync(payment, cancellationToken);

        order.MarkRefunded(fullyRefunded: payment.RefundableAmount() <= 0m);
        await _orderRepository.UpdateAsync(order, cancellationToken);

        return refund;
    }

    public async Task<SavedPaymentMethod> SaveCardAsync(string buyerId, PayPalCardDetails card,
        CancellationToken cancellationToken = default)
    {
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));
        Guard.Against.Null(card, nameof(card));

        var vaulted = await _payPalClient.VaultCardAsync(card,
            $"eshop-vault-{buyerId}-{Guid.NewGuid():N}", cancellationToken);

        var saved = new SavedPaymentMethod(buyerId, vaulted.CustomerId, vaulted.VaultTokenId,
            vaulted.Brand, vaulted.LastDigits, vaulted.Expiry, vaulted.CardholderName);
        return await _paymentMethodRepository.AddAsync(saved, cancellationToken);
    }

    public async Task DeleteSavedCardAsync(SavedPaymentMethod savedPaymentMethod,
        CancellationToken cancellationToken = default)
    {
        Guard.Against.Null(savedPaymentMethod, nameof(savedPaymentMethod));

        try
        {
            await _payPalClient.DeleteVaultedCardAsync(savedPaymentMethod.VaultTokenId, cancellationToken);
        }
        catch (PaymentGatewayException ex)
        {
            // If the token is already gone from PayPal's vault, local removal still proceeds.
            _logger.LogWarning($"Could not delete PayPal vault token for payment method {savedPaymentMethod.Id}: {ex.Message}");
        }

        await _paymentMethodRepository.DeleteAsync(savedPaymentMethod, cancellationToken);
    }
}
