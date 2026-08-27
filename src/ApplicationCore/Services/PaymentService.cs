using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Ardalis.GuardClauses;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Specifications;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.ApplicationCore.Services;

public class PaymentService : IPaymentService
{
    // Unique per application run: keeps PayPal invoice ids and idempotency keys unique even
    // when the local database is reset (in-memory provider) while the PayPal merchant account persists.
    private static readonly string RunId = Guid.NewGuid().ToString("N")[..8];

    private readonly IRepository<Order> _orderRepository;
    private readonly IRepository<Payment> _paymentRepository;
    private readonly IRepository<SavedPaymentMethod> _paymentMethodRepository;
    private readonly IPaymentGateway _gateway;
    private readonly PayPalSettings _settings;
    private readonly IAppLogger<PaymentService> _logger;

    public PaymentService(
        IRepository<Order> orderRepository,
        IRepository<Payment> paymentRepository,
        IRepository<SavedPaymentMethod> paymentMethodRepository,
        IPaymentGateway gateway,
        IOptions<PayPalSettings> settings,
        IAppLogger<PaymentService> logger)
    {
        _orderRepository = orderRepository;
        _paymentRepository = paymentRepository;
        _paymentMethodRepository = paymentMethodRepository;
        _gateway = gateway;
        _settings = settings.Value;
        _logger = logger;
    }

    public async Task<Payment> AuthorizePaymentAsync(string buyerId, int orderId, CardDetails? card, int? savedPaymentMethodId,
        CancellationToken cancellationToken = default)
    {
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));
        if (card is null && savedPaymentMethodId is null)
        {
            throw new PaymentConflictException("Provide either card details or a saved paymentMethodId to pay.");
        }

        var order = await _orderRepository.FirstOrDefaultAsync(new OrderWithItemsByIdSpec(orderId), cancellationToken);
        if (order is null || order.BuyerId != buyerId)
        {
            throw new ResourceNotFoundException($"Order {orderId} was not found.");
        }

        var payment = await _paymentRepository.FirstOrDefaultAsync(new PaymentByOrderIdSpecification(orderId), cancellationToken);

        // Idempotent replay: a hold already exists (or money was already taken) — return it, never hold twice.
        if (payment is not null &&
            (payment.Status == PaymentStatus.Authorized || payment.Status == PaymentStatus.Captured ||
             payment.Status == PaymentStatus.PartiallyRefunded || payment.Status == PaymentStatus.Refunded))
        {
            return payment;
        }

        if (order.Status != OrderStatus.AwaitingPayment)
        {
            throw new PaymentConflictException($"Order {orderId} is in status '{order.Status}' and cannot be paid.");
        }

        var amount = order.Total();
        var currency = RequireCurrency();

        string? vaultTokenId = null;
        if (savedPaymentMethodId is not null)
        {
            var savedMethod = await _paymentMethodRepository.GetByIdAsync(savedPaymentMethodId.Value, cancellationToken);
            if (savedMethod is null || savedMethod.BuyerId != buyerId || !savedMethod.IsActive)
            {
                throw new ResourceNotFoundException($"Payment method {savedPaymentMethodId} was not found.");
            }
            vaultTokenId = savedMethod.VaultTokenId;
        }

        if (payment is null)
        {
            payment = new Payment(order.Id, buyerId, amount, currency);
            payment.IncrementAuthorizationAttempt();
            await _paymentRepository.AddAsync(payment, cancellationToken);
        }
        else
        {
            payment.IncrementAuthorizationAttempt();
            await _paymentRepository.UpdateAsync(payment, cancellationToken);
        }

        var idempotencyKey = $"eshop-{RunId}-order-{orderId}-auth-{payment.AuthorizationAttempt}";
        // PayPal rejects duplicate invoice ids, so each authorization attempt gets its own.
        var invoiceId = $"eshop-{RunId}-order-{orderId}-a{payment.AuthorizationAttempt}";

        GatewayAuthorizationResult result;
        try
        {
            result = vaultTokenId is not null
                ? await _gateway.AuthorizeVaultedCardAsync(vaultTokenId, amount, currency, idempotencyKey, invoiceId, cancellationToken)
                : await _gateway.AuthorizeCardAsync(card!, amount, currency, idempotencyKey, invoiceId, cancellationToken);
        }
        catch
        {
            payment.MarkAuthorizationFailed();
            await _paymentRepository.UpdateAsync(payment, cancellationToken);
            throw;
        }

        payment.MarkAuthorized(result.PayPalOrderId, result.AuthorizationId, result.Status, result.ExpiresAt);
        await _paymentRepository.UpdateAsync(payment, cancellationToken);

        order.MarkPaymentAuthorized();
        await _orderRepository.UpdateAsync(order, cancellationToken);

        _logger.LogInformation($"Authorized {amount:0.00} {currency} for order {orderId} (authorization {result.AuthorizationId}).");
        return payment;
    }

    public async Task<Payment> CapturePaymentAsync(int orderId, CancellationToken cancellationToken = default)
    {
        var order = await _orderRepository.FirstOrDefaultAsync(new OrderWithItemsByIdSpec(orderId), cancellationToken);
        if (order is null)
        {
            throw new ResourceNotFoundException($"Order {orderId} was not found.");
        }

        var payment = await _paymentRepository.FirstOrDefaultAsync(new PaymentByOrderIdSpecification(orderId), cancellationToken);

        // Idempotent replay: already fulfilled — report the existing capture.
        if (order.Status == OrderStatus.Fulfilled)
        {
            if (payment is null || payment.CaptureId is null)
            {
                throw new PaymentConflictException($"Order {orderId} is fulfilled but has no captured payment on record.");
            }
            return payment;
        }

        if (order.Status != OrderStatus.PaymentAuthorized || payment is null || payment.AuthorizationId is null)
        {
            throw new PaymentConflictException(
                $"Order {orderId} is in status '{order.Status}' and has no active authorization to capture. " +
                "The shopper must pay first.");
        }

        var currency = payment.Currency;
        var invoiceId = $"eshop-{RunId}-order-{orderId}-cap-{payment.AuthorizationAttempt}";

        var authorization = await _gateway.GetAuthorizationAsync(payment.AuthorizationId, cancellationToken);
        payment.UpdateAuthorization(authorization.AuthorizationId, authorization.Status, authorization.ExpiresAt);

        if (!IsCapturable(authorization.Status))
        {
            await RenewAuthorizationAsync(payment, orderId, currency, authorization.Status, cancellationToken);
        }

        GatewayCaptureResult capture;
        try
        {
            capture = await _gateway.CaptureAuthorizationAsync(payment.AuthorizationId, payment.Amount, currency,
                $"eshop-{RunId}-order-{orderId}-capture-{payment.AuthorizationId}", invoiceId, cancellationToken);
        }
        catch (PaymentGatewayException)
        {
            // The authorization may have gone stale between the status check and the capture:
            // renew it once and retry before giving up.
            await RenewAuthorizationAsync(payment, orderId, currency, "capture-rejected", cancellationToken);
            capture = await _gateway.CaptureAuthorizationAsync(payment.AuthorizationId, payment.Amount, currency,
                $"eshop-{RunId}-order-{orderId}-capture-{payment.AuthorizationId}", invoiceId, cancellationToken);
        }

        payment.MarkCaptured(capture.CaptureId, capture.Status, capture.GrossAmount, capture.PayPalFee, capture.NetAmount);
        await _paymentRepository.UpdateAsync(payment, cancellationToken);

        order.MarkFulfilled();
        await _orderRepository.UpdateAsync(order, cancellationToken);

        _logger.LogInformation($"Captured {capture.GrossAmount:0.00} {currency} for order {orderId} (capture {capture.CaptureId}).");
        return payment;
    }

    public async Task<Payment?> VoidPaymentAsync(int orderId, CancellationToken cancellationToken = default)
    {
        var order = await _orderRepository.FirstOrDefaultAsync(new OrderWithItemsByIdSpec(orderId), cancellationToken);
        if (order is null)
        {
            throw new ResourceNotFoundException($"Order {orderId} was not found.");
        }

        var payment = await _paymentRepository.FirstOrDefaultAsync(new PaymentByOrderIdSpecification(orderId), cancellationToken);

        // Idempotent replay.
        if (order.Status == OrderStatus.Cancelled)
        {
            return payment;
        }

        if (order.Status == OrderStatus.Fulfilled)
        {
            throw new PaymentConflictException(
                $"Order {orderId} is already fulfilled and the payment was captured; issue a refund instead of cancelling.");
        }

        if (payment is not null && payment.AuthorizationId is not null &&
            payment.Status != PaymentStatus.Voided && payment.Status != PaymentStatus.Failed)
        {
            try
            {
                await _gateway.VoidAuthorizationAsync(payment.AuthorizationId,
                    $"eshop-{RunId}-order-{orderId}-void-{payment.AuthorizationId}", cancellationToken);
            }
            catch (PaymentGatewayException ex) when (ex.StatusCode is System.Net.HttpStatusCode.NotFound
                or System.Net.HttpStatusCode.UnprocessableEntity)
            {
                // Already voided or otherwise not voidable at PayPal — nothing left to release.
                _logger.LogWarning($"Void of authorization {payment.AuthorizationId} for order {orderId} was rejected ({ex.ErrorName}); treating as released.");
            }
            payment.MarkVoided();
            await _paymentRepository.UpdateAsync(payment, cancellationToken);
        }
        else if (payment is not null && payment.Status != PaymentStatus.Voided)
        {
            payment.MarkVoided();
            await _paymentRepository.UpdateAsync(payment, cancellationToken);
        }

        order.MarkCancelled();
        await _orderRepository.UpdateAsync(order, cancellationToken);

        _logger.LogInformation($"Cancelled order {orderId}; any held funds were released.");
        return payment;
    }

    public async Task<PaymentRefund> RefundPaymentAsync(string buyerId, int orderId, decimal? amount, string idempotencyKey,
        CancellationToken cancellationToken = default)
    {
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));
        Guard.Against.NullOrEmpty(idempotencyKey, nameof(idempotencyKey));

        var order = await _orderRepository.FirstOrDefaultAsync(new OrderWithItemsByIdSpec(orderId), cancellationToken);
        if (order is null || order.BuyerId != buyerId)
        {
            throw new ResourceNotFoundException($"Order {orderId} was not found.");
        }

        var payment = await _paymentRepository.FirstOrDefaultAsync(new PaymentByOrderIdSpecification(orderId), cancellationToken);
        if (payment is null || payment.CaptureId is null)
        {
            throw new PaymentConflictException($"Order {orderId} has no captured payment to refund.");
        }

        var existing = payment.Refunds.FirstOrDefault(r => r.IdempotencyKey == idempotencyKey);
        if (existing is not null && existing.Status != PaymentRefund.FailedStatus)
        {
            // Same key replayed — return the original refund, never refund twice.
            return existing;
        }

        var refundAmount = amount ?? payment.RefundableAmount;
        if (refundAmount <= 0m)
        {
            throw new PaymentConflictException($"Order {orderId} is already fully refunded; nothing remains to refund.");
        }

        PaymentRefund refund;
        if (existing is not null)
        {
            refund = existing;
        }
        else
        {
            refund = payment.AddRefund(idempotencyKey, refundAmount);
            await _paymentRepository.UpdateAsync(payment, cancellationToken);
        }

        try
        {
            // The invoice id must be unique per refund at PayPal; the idempotency key keeps it
            // deterministic for retries of the same refund.
            var refundInvoiceId = $"eshop-{RunId}-order-{orderId}-ref-{idempotencyKey}";
            if (refundInvoiceId.Length > 127)
            {
                refundInvoiceId = refundInvoiceId[..127];
            }
            var result = await _gateway.RefundCaptureAsync(payment.CaptureId, refundAmount, payment.Currency,
                $"eshop-{RunId}-refund-{idempotencyKey}", refundInvoiceId, cancellationToken);
            refund.MarkCompleted(result.RefundId, result.Status);
        }
        catch
        {
            refund.MarkFailed();
            await _paymentRepository.UpdateAsync(payment, cancellationToken);
            throw;
        }

        payment.RefreshRefundStatus();
        await _paymentRepository.UpdateAsync(payment, cancellationToken);

        _logger.LogInformation($"Refunded {refund.Amount:0.00} {payment.Currency} for order {orderId} (refund {refund.PayPalRefundId}).");
        return refund;
    }

    private static bool IsCapturable(string? status) =>
        status is "CREATED" or "PENDING";

    private async Task RenewAuthorizationAsync(Payment payment, int orderId, string currency, string currentStatus,
        CancellationToken cancellationToken)
    {
        try
        {
            var renewed = await _gateway.ReauthorizeAsync(payment.AuthorizationId!, payment.Amount, currency,
                $"eshop-{RunId}-order-{orderId}-reauth-{payment.AuthorizationId}", cancellationToken);
            payment.UpdateAuthorization(renewed.AuthorizationId, renewed.Status, renewed.ExpiresAt);
            await _paymentRepository.UpdateAsync(payment, cancellationToken);
            _logger.LogInformation($"Renewed stale authorization for order {orderId} (authorization {renewed.AuthorizationId}).");
        }
        catch (PaymentGatewayException ex)
        {
            throw new PaymentConflictException(
                $"The PayPal authorization {payment.AuthorizationId} for order {orderId} is stale (status '{currentStatus}') " +
                $"and can no longer be renewed ({ex.ErrorName ?? ex.Message}). " +
                $"Void this order and ask the shopper to pay again via POST /api/orders/{orderId}/pay.");
        }
    }

    private string RequireCurrency() =>
        string.IsNullOrWhiteSpace(_settings.Currency)
            ? throw new PaymentGatewayException("PayPal:Currency is not configured.")
            : _settings.Currency!;
}
