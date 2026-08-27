using System;
using System.Threading;
using System.Threading.Tasks;
using Ardalis.GuardClauses;
using Microsoft.eShopWeb.ApplicationCore.Configuration;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Models.Payments;
using Microsoft.eShopWeb.ApplicationCore.Specifications;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.ApplicationCore.Services;

public class PaymentService : IPaymentService
{
    // PayPal stores PayPal-Request-Id keys for hours; a process-unique prefix keeps
    // keys from colliding across restarts (in-memory ids restart from 1).
    private static readonly string RunId = Guid.NewGuid().ToString("N")[..8];

    private readonly IRepository<Order> _orderRepository;
    private readonly IRepository<Payment> _paymentRepository;
    private readonly IRepository<SavedCard> _savedCardRepository;
    private readonly IPaymentGateway _gateway;
    private readonly PayPalSettings _settings;
    private readonly IAppLogger<PaymentService> _logger;

    public PaymentService(IRepository<Order> orderRepository,
        IRepository<Payment> paymentRepository,
        IRepository<SavedCard> savedCardRepository,
        IPaymentGateway gateway,
        IOptions<PayPalSettings> settings,
        IAppLogger<PaymentService> logger)
    {
        _orderRepository = orderRepository;
        _paymentRepository = paymentRepository;
        _savedCardRepository = savedCardRepository;
        _gateway = gateway;
        _settings = settings.Value;
        _logger = logger;
    }

    public async Task<Payment> AuthorizePaymentAsync(string buyerId, int orderId, CardDetails? card, int? savedCardId,
        CancellationToken cancellationToken = default)
    {
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));
        if (card == null && savedCardId == null)
            throw new PaymentConflictException("Provide either card details or a saved card (paymentMethodId) to pay.");
        if (card != null && savedCardId != null)
            throw new PaymentConflictException("Provide either card details or a saved card (paymentMethodId), not both.");

        var order = await GetOwnedOrderAsync(buyerId, orderId, cancellationToken);
        var existing = await _paymentRepository.FirstOrDefaultAsync(new PaymentByOrderIdSpec(orderId), cancellationToken);

        if (order.Status == OrderStatus.PaymentAuthorized && existing?.Status == PaymentStatus.Authorized)
        {
            // Idempotent retry of a successful pay call.
            return existing;
        }
        if (order.Status != OrderStatus.PendingPayment)
            throw new PaymentConflictException($"Order {orderId} is '{order.Status}' and cannot be paid.");

        string? vaultTokenId = null;
        if (savedCardId != null)
        {
            var savedCard = await _savedCardRepository.FirstOrDefaultAsync(new SavedCardByIdSpec(savedCardId.Value), cancellationToken);
            if (savedCard == null || savedCard.BuyerId != buyerId)
                throw new EntityNotFoundException($"Payment method {savedCardId} not found.");
            vaultTokenId = savedCard.VaultTokenId;
        }

        var amount = order.Total();
        var customId = $"eshop-order-{orderId}";
        var idempotencyKey = $"eshop-{RunId}-order-{orderId}-authorize";

        GatewayAuthorizationResult authorization = vaultTokenId != null
            ? await _gateway.AuthorizeVaultedCardPaymentAsync(amount, _settings.Currency, vaultTokenId, customId, idempotencyKey, cancellationToken)
            : await _gateway.AuthorizeCardPaymentAsync(amount, _settings.Currency, card!, customId, idempotencyKey, cancellationToken);

        var payment = existing ?? new Payment(orderId, buyerId, amount, _settings.Currency);
        payment.MarkAuthorized(authorization.PayPalOrderId, authorization.AuthorizationId,
            authorization.Status, authorization.ExpiresAt, savedCardId);
        order.MarkPaymentAuthorized();

        if (existing == null)
            await _paymentRepository.AddAsync(payment, cancellationToken);
        else
            await _paymentRepository.UpdateAsync(payment, cancellationToken);
        await _orderRepository.UpdateAsync(order, cancellationToken);

        _logger.LogInformation($"Order {orderId} authorized: PayPal order {authorization.PayPalOrderId}, authorization {authorization.AuthorizationId}.");
        return payment;
    }

    public async Task<Payment> CapturePaymentAsync(int orderId, CancellationToken cancellationToken = default)
    {
        var order = await _orderRepository.FirstOrDefaultAsync(new OrderWithItemsByIdSpec(orderId), cancellationToken);
        if (order == null)
            throw new EntityNotFoundException($"Order {orderId} not found.");

        var payment = await _paymentRepository.FirstOrDefaultAsync(new PaymentByOrderIdSpec(orderId), cancellationToken);

        if (order.Status == OrderStatus.Fulfilled && payment?.Status == PaymentStatus.Captured)
        {
            // Idempotent retry of a successful fulfil.
            return payment;
        }
        if (order.Status != OrderStatus.PaymentAuthorized || payment?.Status != PaymentStatus.Authorized || payment.AuthorizationId == null)
            throw new PaymentConflictException($"Order {orderId} is '{order.Status}' and cannot be fulfilled. Only paid (authorized) orders can be fulfilled.");

        var authorization = await _gateway.GetAuthorizationAsync(payment.AuthorizationId, cancellationToken);
        if (!IsCapturable(authorization.Status))
        {
            // The hold went stale before fulfilment: renew it rather than failing outright.
            _logger.LogInformation($"Authorization {payment.AuthorizationId} for order {orderId} is '{authorization.Status}'; attempting reauthorization.");
            try
            {
                var renewed = await _gateway.ReauthorizeAsync(payment.AuthorizationId, payment.Amount, payment.Currency,
                    $"eshop-{RunId}-order-{orderId}-reauthorize-{payment.AuthorizationId}", cancellationToken);
                payment.MarkAuthorizationRenewed(renewed.AuthorizationId, renewed.Status, renewed.ExpiresAt);
                await _paymentRepository.UpdateAsync(payment, cancellationToken);
            }
            catch (PaymentGatewayException ex)
            {
                throw new PaymentConflictException(
                    $"The PayPal authorization for order {orderId} is no longer capturable and could not be renewed ({ex.Message}). " +
                    "Release this order and ask the shopper to pay again before fulfilling.");
            }
        }

        GatewayCaptureResult capture;
        try
        {
            capture = await _gateway.CaptureAuthorizationAsync(payment.AuthorizationId, payment.Amount, payment.Currency,
                $"eshop-{RunId}-order-{orderId}-capture-{payment.AuthorizationId}", cancellationToken);
        }
        catch (PaymentGatewayException ex)
        {
            throw new PaymentConflictException(
                $"PayPal declined the capture for order {orderId}: {ex.Message}. The order remains paid (authorized); retry fulfilment or cancel to release the hold.");
        }

        payment.MarkCaptured(capture.CaptureId, capture.GrossAmount, capture.PayPalFee, capture.NetAmount);
        order.MarkFulfilled();

        await _paymentRepository.UpdateAsync(payment, cancellationToken);
        await _orderRepository.UpdateAsync(order, cancellationToken);

        _logger.LogInformation($"Order {orderId} fulfilled: capture {capture.CaptureId}, gross {capture.GrossAmount}, fee {capture.PayPalFee}, net {capture.NetAmount} {capture.Currency}.");
        return payment;
    }

    public async Task<Payment?> CancelPaymentAsync(int orderId, CancellationToken cancellationToken = default)
    {
        var order = await _orderRepository.FirstOrDefaultAsync(new OrderWithItemsByIdSpec(orderId), cancellationToken);
        if (order == null)
            throw new EntityNotFoundException($"Order {orderId} not found.");

        var payment = await _paymentRepository.FirstOrDefaultAsync(new PaymentByOrderIdSpec(orderId), cancellationToken);

        if (order.Status == OrderStatus.Cancelled)
        {
            // Idempotent retry of a successful cancel.
            return payment;
        }
        if (order.Status == OrderStatus.Fulfilled)
            throw new PaymentConflictException($"Order {orderId} is already fulfilled and cannot be cancelled. Issue a refund instead.");

        if (payment?.Status == PaymentStatus.Authorized && payment.AuthorizationId != null)
        {
            await _gateway.VoidAuthorizationAsync(payment.AuthorizationId, cancellationToken);
            payment.MarkVoided();
            await _paymentRepository.UpdateAsync(payment, cancellationToken);
            _logger.LogInformation($"Order {orderId} cancelled: authorization {payment.AuthorizationId} voided, held funds released.");
        }

        order.MarkCancelled();
        await _orderRepository.UpdateAsync(order, cancellationToken);
        return payment;
    }

    public async Task<PaymentRefund> RefundPaymentAsync(string buyerId, int orderId, decimal? amount, string idempotencyKey,
        CancellationToken cancellationToken = default)
    {
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));
        Guard.Against.NullOrEmpty(idempotencyKey, nameof(idempotencyKey));

        var order = await GetOwnedOrderAsync(buyerId, orderId, cancellationToken);
        var payment = await _paymentRepository.FirstOrDefaultAsync(new PaymentByOrderIdSpec(orderId), cancellationToken);

        if (payment != null)
        {
            var existingRefund = System.Linq.Enumerable.FirstOrDefault(payment.Refunds, r => r.IdempotencyKey == idempotencyKey);
            if (existingRefund != null)
            {
                // Same idempotency key: return the original refund, never refund twice.
                return existingRefund;
            }
        }

        if (order.Status != OrderStatus.Fulfilled || payment?.Status != PaymentStatus.Captured || payment.CaptureId == null)
            throw new PaymentConflictException($"Order {orderId} is '{order.Status}' and cannot be refunded. Only fulfilled orders can be refunded.");

        var refundAmount = amount ?? payment.RefundableAmount;
        if (refundAmount <= 0)
            throw new PaymentConflictException("Refund amount must be positive.");
        if (refundAmount > payment.RefundableAmount)
            throw new PaymentConflictException(
                $"Refund of {refundAmount} {payment.Currency} exceeds the refundable amount of {payment.RefundableAmount} {payment.Currency} for order {orderId}.");

        var result = await _gateway.RefundCaptureAsync(payment.CaptureId, refundAmount, payment.Currency,
            idempotencyKey, $"Refund for eShop order {orderId}", cancellationToken);

        var refund = payment.AddRefund(result.RefundId, result.Amount, idempotencyKey, result.Status);
        await _paymentRepository.UpdateAsync(payment, cancellationToken);

        _logger.LogInformation($"Order {orderId} refunded: refund {result.RefundId}, amount {result.Amount} {result.Currency}.");
        return refund;
    }

    public async Task<Payment?> GetPaymentForOrderAsync(string buyerId, int orderId, CancellationToken cancellationToken = default)
    {
        var order = await GetOwnedOrderAsync(buyerId, orderId, cancellationToken);
        return await _paymentRepository.FirstOrDefaultAsync(new PaymentByOrderIdSpec(order.Id), cancellationToken);
    }

    private async Task<Order> GetOwnedOrderAsync(string buyerId, int orderId, CancellationToken cancellationToken)
    {
        var order = await _orderRepository.FirstOrDefaultAsync(new OrderWithItemsByIdSpec(orderId), cancellationToken);
        // Existence of other shoppers' orders is not leaked: same 404 as a missing order.
        if (order == null || order.BuyerId != buyerId)
            throw new EntityNotFoundException($"Order {orderId} not found.");
        return order;
    }

    private static bool IsCapturable(string authorizationStatus)
        => authorizationStatus == "CREATED" || authorizationStatus == "PENDING";
}
