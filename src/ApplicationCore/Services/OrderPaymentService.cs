using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentMethodAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Specifications;

namespace Microsoft.eShopWeb.ApplicationCore.Services;

public class OrderPaymentService : IOrderPaymentService
{
    private readonly IRepository<OrderPayment> _paymentRepository;
    private readonly IReadRepository<Order> _orderRepository;
    private readonly IReadRepository<SavedPaymentMethod> _savedCardRepository;
    private readonly IPayPalPaymentGateway _gateway;
    private readonly IAppLogger<OrderPaymentService> _logger;

    public OrderPaymentService(
        IRepository<OrderPayment> paymentRepository,
        IReadRepository<Order> orderRepository,
        IReadRepository<SavedPaymentMethod> savedCardRepository,
        IPayPalPaymentGateway gateway,
        IAppLogger<OrderPaymentService> logger)
    {
        _paymentRepository = paymentRepository;
        _orderRepository = orderRepository;
        _savedCardRepository = savedCardRepository;
        _gateway = gateway;
        _logger = logger;
    }

    public async Task<OrderPayment> AuthorizeAsync(int orderId, string buyerId, PaymentCard? card,
        int? savedPaymentMethodId, CancellationToken cancellationToken = default)
    {
        var payment = await LoadOwnedPaymentAsync(orderId, buyerId, cancellationToken);

        // Idempotent in effect: a double-click never authorizes twice.
        if (payment.Status == PaymentStatus.Authorized)
        {
            return payment;
        }
        if (payment.Status is PaymentStatus.Captured or PaymentStatus.PartiallyRefunded or PaymentStatus.Refunded)
        {
            throw new PaymentStateException($"Order {orderId} has already been paid and cannot be authorized again.");
        }
        if (payment.Status == PaymentStatus.Cancelled)
        {
            throw new PaymentStateException($"Order {orderId} has been cancelled and cannot be paid.");
        }

        string? vaultId = null;
        if (savedPaymentMethodId.HasValue)
        {
            if (card is not null)
            {
                throw new InvalidPaymentRequestException("Provide either card details or a saved card, not both.");
            }
            var savedCard = await _savedCardRepository.GetByIdAsync(savedPaymentMethodId.Value, cancellationToken)
                ?? throw new PaymentMethodNotFoundException(savedPaymentMethodId.Value);
            if (!string.Equals(savedCard.BuyerId, buyerId, StringComparison.Ordinal))
            {
                // Don't reveal another shopper's card even by existence.
                throw new PaymentMethodNotFoundException(savedPaymentMethodId.Value);
            }
            vaultId = savedCard.PayPalVaultTokenId;
        }
        else if (card is null)
        {
            throw new InvalidPaymentRequestException("A card or a saved payment method is required to pay.");
        }

        // Retrying a previously failed payment gets fresh idempotency ids so PayPal treats it anew.
        if (payment.Status == PaymentStatus.Failed)
        {
            payment.PrepareRetry();
        }

        var request = new AuthorizeRequest(
            Amount: payment.Amount,
            InvoiceId: payment.PayPalInvoiceId!,
            IdempotencyKey: payment.AuthorizeRequestId,
            Card: card,
            VaultId: vaultId);

        AuthorizeResult result;
        try
        {
            result = await _gateway.AuthorizeAsync(request, cancellationToken);
        }
        catch (PayPalApiException ex)
        {
            _logger.LogWarning($"Authorization failed for order {orderId}: {ex.Message}");
            payment.MarkFailed();
            await _paymentRepository.UpdateAsync(payment, cancellationToken);
            throw;
        }

        payment.MarkAuthorized(result.PayPalOrderId, result.AuthorizationId, result.Status,
            result.ExpiresAt, savedPaymentMethodId);
        await _paymentRepository.UpdateAsync(payment, cancellationToken);
        return payment;
    }

    public async Task<OrderPayment> FulfilAsync(int orderId, CancellationToken cancellationToken = default)
    {
        var payment = await LoadPaymentAsync(orderId, cancellationToken);

        // Idempotent: fulfilling an already-captured order returns its current state.
        if (payment.Status is PaymentStatus.Captured or PaymentStatus.PartiallyRefunded or PaymentStatus.Refunded)
        {
            return payment;
        }
        if (payment.Status != PaymentStatus.Authorized)
        {
            throw new PaymentStateException(
                $"Order {orderId} is {payment.Status}; it must be authorized before it can be fulfilled.");
        }

        var authorizationId = payment.AuthorizationId!;

        // Renew a stale hold rather than failing fulfilment outright.
        var details = await _gateway.GetAuthorizationAsync(authorizationId, cancellationToken);
        if (IsTerminalUncapturable(details.Status))
        {
            throw new AuthorizationRenewalException(
                $"The hold on order {orderId} is {details.Status} and can no longer be captured or renewed. " +
                $"Ask the shopper to pay order {orderId} again before fulfilling it.");
        }
        if (IsStale(details))
        {
            authorizationId = await RenewOrThrowAsync(payment, orderId, null, cancellationToken);
        }

        CaptureResult capture;
        try
        {
            capture = await CaptureAsync(payment, authorizationId, orderId, cancellationToken);
        }
        catch (PayPalApiException ex) when (IsExpiryIssue(ex))
        {
            // The hold went stale between our check and the capture; renew once and retry.
            authorizationId = await RenewOrThrowAsync(payment, orderId, ex, cancellationToken);
            capture = await CaptureAsync(payment, authorizationId, orderId, cancellationToken);
        }

        payment.MarkCaptured(capture.CaptureId, capture.Status, capture.Gross, capture.Fee, capture.Net);
        await _paymentRepository.UpdateAsync(payment, cancellationToken);
        return payment;
    }

    public async Task<OrderPayment> CancelAsync(int orderId, CancellationToken cancellationToken = default)
    {
        var payment = await LoadPaymentAsync(orderId, cancellationToken);

        if (payment.Status == PaymentStatus.Cancelled)
        {
            return payment;
        }
        if (payment.Status is PaymentStatus.Captured or PaymentStatus.PartiallyRefunded or PaymentStatus.Refunded)
        {
            throw new PaymentStateException(
                $"Order {orderId} has already been fulfilled; use a refund to return money.");
        }

        if (payment.Status == PaymentStatus.Authorized && !string.IsNullOrEmpty(payment.AuthorizationId))
        {
            await _gateway.VoidAuthorizationAsync(payment.AuthorizationId, cancellationToken);
        }

        payment.MarkCancelled();
        await _paymentRepository.UpdateAsync(payment, cancellationToken);
        return payment;
    }

    public async Task<PaymentRefund> RefundAsync(int orderId, string buyerId, decimal? amount,
        string idempotencyKey, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(idempotencyKey))
        {
            throw new InvalidPaymentRequestException("A refund requires an idempotency key.");
        }

        var payment = await LoadOwnedPaymentAsync(orderId, buyerId, cancellationToken);

        // Repeating a refund under the same key must not refund twice.
        var existing = payment.FindRefundByIdempotencyKey(idempotencyKey);
        if (existing is not null)
        {
            return existing;
        }

        if (payment.Status is not (PaymentStatus.Captured or PaymentStatus.PartiallyRefunded))
        {
            throw new PaymentStateException(
                $"Order {orderId} is {payment.Status}; only a captured order can be refunded.");
        }

        var remaining = payment.RefundableRemaining();
        var refundAmount = amount ?? remaining;
        if (refundAmount <= 0m)
        {
            throw new InvalidPaymentRequestException("Refund amount must be greater than zero.");
        }
        if (refundAmount > remaining)
        {
            throw new PaymentStateException(
                $"Refund of {refundAmount:0.00} exceeds the remaining refundable amount of {remaining:0.00} for order {orderId}.");
        }

        // Scope the PayPal-Request-Id to this capture so the same caller key used on a different
        // order never collides in PayPal's global idempotency space. Repeats are already caught
        // above by our own stored key, so PayPal is only ever hit once per (capture, key).
        var payPalRequestId = $"{payment.CaptureId}-{idempotencyKey}";
        var result = await _gateway.RefundAsync(payment.CaptureId!, refundAmount,
            payment.PayPalInvoiceId ?? OrderInvoiceReference.New(orderId), payPalRequestId, cancellationToken);

        var refund = new PaymentRefund(idempotencyKey, refundAmount, result.PayPalRefundId, result.Status);
        payment.AddRefund(refund);
        await _paymentRepository.UpdateAsync(payment, cancellationToken);
        return refund;
    }

    public async Task<IReadOnlyList<OrderPaymentSummary>> GetOrdersForBuyerAsync(string buyerId,
        CancellationToken cancellationToken = default)
    {
        var payments = await _paymentRepository.ListAsync(new OrderPaymentsByBuyerSpec(buyerId), cancellationToken);
        var orders = await _orderRepository.ListAsync(new CustomerOrdersWithItemsSpecification(buyerId), cancellationToken);
        var ordersById = orders.ToDictionary(o => o.Id);

        return payments
            .OrderByDescending(p => p.CreatedDate)
            .Select(p =>
            {
                ordersById.TryGetValue(p.OrderId, out var order);
                return new OrderPaymentSummary(
                    OrderId: p.OrderId,
                    OrderDate: order?.OrderDate ?? p.CreatedDate,
                    Total: order?.Total() ?? p.Amount,
                    Currency: p.CurrencyCode,
                    PaymentStatus: p.Status.ToString(),
                    PayPalOrderId: p.PayPalOrderId,
                    AuthorizationId: p.AuthorizationId,
                    AuthorizationStatus: p.AuthorizationStatus,
                    AuthorizationExpiresAt: p.AuthorizationExpiresAt,
                    CaptureId: p.CaptureId,
                    CapturedGross: p.CapturedGross,
                    PayPalFee: p.PayPalFee,
                    NetAmount: p.NetAmount,
                    RefundedToDate: p.RefundedToDate(),
                    SavedPaymentMethodId: p.SavedPaymentMethodId);
            })
            .ToList();
    }

    // ---- helpers ------------------------------------------------------------

    private async Task<CaptureResult> CaptureAsync(OrderPayment payment, string authorizationId, int orderId,
        CancellationToken cancellationToken) =>
        await _gateway.CaptureAsync(authorizationId, payment.Amount,
            payment.PayPalInvoiceId ?? OrderInvoiceReference.New(orderId), payment.CaptureRequestId, cancellationToken);

    private async Task<string> RenewOrThrowAsync(OrderPayment payment, int orderId, PayPalApiException? cause,
        CancellationToken cancellationToken)
    {
        try
        {
            var reauth = await _gateway.ReauthorizeAsync(payment.AuthorizationId!, payment.Amount,
                $"reauthorize-order-{orderId}", cancellationToken);
            payment.ReplaceAuthorization(reauth.AuthorizationId, reauth.Status, reauth.ExpiresAt);
            await _paymentRepository.UpdateAsync(payment, cancellationToken);
            _logger.LogInformation($"Renewed the authorization for order {orderId} before fulfilment.");
            return reauth.AuthorizationId;
        }
        catch (PayPalApiException ex)
        {
            throw new AuthorizationRenewalException(
                $"The authorization for order {orderId} has expired and could not be renewed ({ex.PayPalIssue ?? "see PayPal"}). " +
                $"Ask the shopper to pay order {orderId} again before fulfilling it.", ex);
        }
    }

    private static bool IsStale(AuthorizationDetails details) =>
        string.Equals(details.Status, "EXPIRED", StringComparison.OrdinalIgnoreCase) ||
        (details.ExpiresAt.HasValue && details.ExpiresAt.Value <= DateTimeOffset.UtcNow);

    private static bool IsTerminalUncapturable(string status) =>
        string.Equals(status, "VOIDED", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(status, "DENIED", StringComparison.OrdinalIgnoreCase);

    private static bool IsExpiryIssue(PayPalApiException ex) =>
        ex.PayPalIssue is not null &&
        (ex.PayPalIssue.Contains("EXPIRED", StringComparison.OrdinalIgnoreCase) ||
         ex.PayPalIssue.Contains("AUTHORIZATION", StringComparison.OrdinalIgnoreCase));

    private async Task<OrderPayment> LoadPaymentAsync(int orderId, CancellationToken cancellationToken) =>
        await _paymentRepository.FirstOrDefaultAsync(new OrderPaymentByOrderIdSpec(orderId), cancellationToken)
        ?? throw new OrderNotFoundException(orderId);

    private async Task<OrderPayment> LoadOwnedPaymentAsync(int orderId, string buyerId, CancellationToken cancellationToken)
    {
        var payment = await LoadPaymentAsync(orderId, cancellationToken);
        if (!string.Equals(payment.BuyerId, buyerId, StringComparison.Ordinal))
        {
            throw new ResourceForbiddenException($"Order {orderId} does not belong to the current user.");
        }
        return payment;
    }
}
