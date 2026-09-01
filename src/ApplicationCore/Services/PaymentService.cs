using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Configuration;
using Microsoft.eShopWeb.ApplicationCore.Entities;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Models.PayPal;
using Microsoft.eShopWeb.ApplicationCore.Specifications;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.ApplicationCore.Services;

public class PaymentService : IPaymentService
{
    private readonly IRepository<Order> _orderRepository;
    private readonly IRepository<Payment> _paymentRepository;
    private readonly IRepository<SavedPaymentMethod> _savedPaymentMethodRepository;
    private readonly IPayPalClient _payPalClient;
    private readonly PayPalSettings _settings;
    private readonly IAppLogger<PaymentService> _logger;

    public PaymentService(
        IRepository<Order> orderRepository,
        IRepository<Payment> paymentRepository,
        IRepository<SavedPaymentMethod> savedPaymentMethodRepository,
        IPayPalClient payPalClient,
        IOptions<PayPalSettings> settings,
        IAppLogger<PaymentService> logger)
    {
        _orderRepository = orderRepository;
        _paymentRepository = paymentRepository;
        _savedPaymentMethodRepository = savedPaymentMethodRepository;
        _payPalClient = payPalClient;
        _settings = settings.Value;
        _logger = logger;
    }

    public async Task<Payment> PayOrderAsync(string buyerId, int orderId, PayPalCard? card, int? savedPaymentMethodId,
        CancellationToken cancellationToken = default)
    {
        if (card is null && savedPaymentMethodId is null)
        {
            throw new PaymentException("Provide either card details or a saved paymentMethodId to pay the order.");
        }

        var order = await _orderRepository.FirstOrDefaultAsync(new OrderWithItemsByIdSpec(orderId), cancellationToken);
        if (order is null || order.BuyerId != buyerId)
        {
            throw new NotFoundException($"Order {orderId} was not found.");
        }

        var existing = await _paymentRepository.FirstOrDefaultAsync(new PaymentByOrderIdSpec(orderId), cancellationToken);
        if (existing is not null)
        {
            return existing.Status switch
            {
                PaymentStatus.Authorized => existing, // idempotent: the hold already exists
                PaymentStatus.Voided => throw new PaymentException(
                    $"Order {orderId} was cancelled and its held funds were released. Place a new order to pay again."),
                _ => throw new PaymentException(
                    $"Order {orderId} has already been captured; it cannot be paid again.")
            };
        }

        string? vaultTokenId = null;
        int? usedSavedPaymentMethodId = null;
        if (savedPaymentMethodId is not null)
        {
            var savedMethod = await _savedPaymentMethodRepository.FirstOrDefaultAsync(
                new SavedPaymentMethodByIdSpec(savedPaymentMethodId.Value), cancellationToken);
            if (savedMethod is null || savedMethod.BuyerId != buyerId)
            {
                throw new NotFoundException($"Saved payment method {savedPaymentMethodId} was not found.");
            }

            vaultTokenId = savedMethod.VaultTokenId;
            usedSavedPaymentMethodId = savedMethod.Id;
        }

        var amount = order.Total();
        var currency = _settings.Currency;
        var requestId = $"eshop-order-{orderId}-pay-{Guid.NewGuid():N}";
        // PayPal enforces invoice_id uniqueness per merchant account, so make it unique per attempt.
        var invoiceId = $"eshop-order-{orderId}-{Guid.NewGuid().ToString("N")[..8]}";

        var payPalOrderId = await _payPalClient.CreateOrderAsync(amount, currency,
            referenceId: orderId.ToString(), invoiceId: invoiceId,
            requestId + "-create", cancellationToken);

        var authorization = await _payPalClient.AuthorizeOrderAsync(payPalOrderId,
            card, vaultTokenId, requestId + "-auth", cancellationToken);

        if (!string.Equals(authorization.Status, "CREATED", StringComparison.OrdinalIgnoreCase)
            && !string.Equals(authorization.Status, "PENDING", StringComparison.OrdinalIgnoreCase))
        {
            throw new PaymentException(
                $"PayPal did not authorize the payment for order {orderId} (status: {authorization.Status}). " +
                "No funds were held; the shopper may retry with a different payment method.");
        }

        var payment = new Payment(orderId, buyerId, authorization.Amount, currency,
            payPalOrderId, authorization.Id, authorization.Status, authorization.ExpiresAt,
            usedSavedPaymentMethodId, invoiceId);
        await _paymentRepository.AddAsync(payment, cancellationToken);

        _logger.LogInformation("Order {OrderId} authorized via PayPal authorization {AuthorizationId}",
            orderId, authorization.Id);
        return payment;
    }

    public async Task<Payment> FulfillOrderAsync(int orderId, CancellationToken cancellationToken = default)
    {
        var payment = await _paymentRepository.FirstOrDefaultAsync(new PaymentByOrderIdSpec(orderId), cancellationToken);
        if (payment is null)
        {
            throw new NotFoundException($"Order {orderId} was not found or has no payment.");
        }

        if (payment.Status is PaymentStatus.Captured or PaymentStatus.PartiallyRefunded or PaymentStatus.Refunded)
        {
            return payment; // idempotent: already fulfilled
        }

        if (payment.Status == PaymentStatus.Voided)
        {
            throw new PaymentException(
                $"Order {orderId} was cancelled and its authorization voided; it cannot be fulfilled. " +
                "Ask the shopper to place and pay a new order.");
        }

        var authorization = await _payPalClient.GetAuthorizationAsync(payment.AuthorizationId, cancellationToken);

        PayPalCapture capture;
        if (string.Equals(authorization.Status, "CREATED", StringComparison.OrdinalIgnoreCase))
        {
            capture = await TryCaptureAsync(payment, cancellationToken);
        }
        else
        {
            capture = await RenewAndCaptureAsync(payment, authorization.Status, cancellationToken);
        }

        if (!string.Equals(capture.Status, "COMPLETED", StringComparison.OrdinalIgnoreCase)
            && !string.Equals(capture.Status, "PENDING", StringComparison.OrdinalIgnoreCase))
        {
            throw new PaymentException(
                $"PayPal capture for order {orderId} ended in status {capture.Status}. " +
                "Do not ship; investigate the payment in the PayPal dashboard before retrying fulfilment.");
        }

        payment.MarkCaptured(capture.Id, capture.GrossAmount, capture.PayPalFee, capture.NetAmount);
        await _paymentRepository.UpdateAsync(payment, cancellationToken);

        _logger.LogInformation("Order {OrderId} fulfilled; captured {Amount} {Currency} (capture {CaptureId})",
            orderId, capture.GrossAmount, payment.Currency, capture.Id);
        return payment;
    }

    public async Task<Payment> CancelOrderAsync(int orderId, CancellationToken cancellationToken = default)
    {
        var payment = await _paymentRepository.FirstOrDefaultAsync(new PaymentByOrderIdSpec(orderId), cancellationToken);
        if (payment is null)
        {
            throw new NotFoundException($"Order {orderId} was not found or has no payment.");
        }

        if (payment.Status == PaymentStatus.Voided)
        {
            return payment; // idempotent: already cancelled
        }

        if (payment.Status != PaymentStatus.Authorized)
        {
            throw new PaymentException(
                $"Order {orderId} has already been captured, so it cannot be cancelled. " +
                "Issue a refund instead (POST /api/orders/{orderId}/refunds).");
        }

        await _payPalClient.VoidAuthorizationAsync(payment.AuthorizationId,
            $"eshop-order-{orderId}-void-{Guid.NewGuid():N}", cancellationToken);

        payment.MarkVoided();
        await _paymentRepository.UpdateAsync(payment, cancellationToken);

        _logger.LogInformation("Order {OrderId} cancelled; authorization {AuthorizationId} voided",
            orderId, payment.AuthorizationId);
        return payment;
    }

    public async Task<PaymentRefundOutcome> RefundOrderAsync(int orderId, decimal? amount, string idempotencyKey,
        string? noteToPayer, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(idempotencyKey))
        {
            throw new PaymentException("An idempotencyKey is required to issue a refund.");
        }

        var payment = await _paymentRepository.FirstOrDefaultAsync(new PaymentByOrderIdSpec(orderId), cancellationToken);
        if (payment is null)
        {
            throw new NotFoundException($"Order {orderId} was not found or has no payment.");
        }

        var existingRefund = payment.Refunds.FirstOrDefault(r => r.IdempotencyKey == idempotencyKey);
        if (existingRefund is not null)
        {
            return new PaymentRefundOutcome(payment, existingRefund, true);
        }

        if (payment.Status is not (PaymentStatus.Captured or PaymentStatus.PartiallyRefunded))
        {
            throw new PaymentException(
                $"Order {orderId} has no captured funds to refund (payment status: {payment.Status}). " +
                "Only fulfilled orders can be refunded.");
        }

        var refundable = payment.RefundableAmount;
        var refundAmount = amount ?? refundable;
        if (refundAmount <= 0m || refundAmount > refundable)
        {
            throw new PaymentException(
                $"Refund amount {refundAmount:0.00} exceeds the refundable remainder {refundable:0.00} " +
                $"{payment.Currency} for order {orderId}.");
        }

        // Scope the PayPal idempotency key to this capture so caller keys stay deterministic
        // per logical refund yet cannot collide with other integrations on a shared account.
        var refund = await _payPalClient.RefundCaptureAsync(payment.CaptureId!, refundAmount, payment.Currency,
            noteToPayer, $"eshop-refund-{payment.CaptureId}-{idempotencyKey}", cancellationToken);

        var entity = payment.AddRefund(refund.Id, refund.Amount, refund.Status, idempotencyKey);
        await _paymentRepository.UpdateAsync(payment, cancellationToken);

        _logger.LogInformation("Order {OrderId} refunded {Amount} {Currency} (refund {RefundId})",
            orderId, refund.Amount, payment.Currency, refund.Id);
        return new PaymentRefundOutcome(payment, entity, false);
    }

    private async Task<PayPalCapture> TryCaptureAsync(Payment payment, CancellationToken cancellationToken)
    {
        try
        {
            return await CaptureAsync(payment, cancellationToken);
        }
        catch (PayPalApiException ex) when (IsAuthorizationStale(ex))
        {
            return await RenewAndCaptureAsync(payment, ex.Issue ?? ex.ErrorName ?? "unknown", cancellationToken);
        }
    }

    private async Task<PayPalCapture> RenewAndCaptureAsync(Payment payment, string staleReason,
        CancellationToken cancellationToken)
    {
        PayPalAuthorization renewed;
        try
        {
            renewed = await _payPalClient.ReauthorizeAsync(payment.AuthorizationId,
                payment.AuthorizedAmount, payment.Currency,
                $"eshop-order-{payment.OrderId}-reauth-{Guid.NewGuid():N}", cancellationToken);
        }
        catch (PayPalApiException ex)
        {
            throw new PaymentException(
                $"The PayPal authorization for order {payment.OrderId} went stale ({staleReason}) and could not be " +
                $"renewed (PayPal: {ex.Issue ?? ex.ErrorName ?? ex.Message}). The held funds were released by PayPal. " +
                "Do not fulfil this order; ask the shopper to pay again or cancel the order.");
        }

        payment.RenewAuthorization(renewed.Id, renewed.Status, renewed.ExpiresAt);
        await _paymentRepository.UpdateAsync(payment, cancellationToken);

        _logger.LogInformation("Order {OrderId} authorization renewed as {AuthorizationId}",
            payment.OrderId, renewed.Id);
        return await CaptureAsync(payment, cancellationToken);
    }

    private Task<PayPalCapture> CaptureAsync(Payment payment, CancellationToken cancellationToken)
    {
        return _payPalClient.CaptureAuthorizationAsync(payment.AuthorizationId,
            payment.AuthorizedAmount, payment.Currency, payment.InvoiceId + "-capture",
            $"eshop-order-{payment.OrderId}-capture-{Guid.NewGuid():N}", cancellationToken);
    }

    private static bool IsAuthorizationStale(PayPalApiException ex)
    {
        var text = $"{ex.Issue} {ex.ErrorName} {ex.Message}";
        return text.Contains("EXPIRED", StringComparison.OrdinalIgnoreCase)
            || text.Contains("AUTHORIZATION", StringComparison.OrdinalIgnoreCase)
               && text.Contains("INVALID", StringComparison.OrdinalIgnoreCase);
    }
}
