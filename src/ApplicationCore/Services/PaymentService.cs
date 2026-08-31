using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Ardalis.GuardClauses;
using Microsoft.eShopWeb.ApplicationCore.Entities.BuyerAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Models;
using Microsoft.eShopWeb.ApplicationCore.Specifications;

namespace Microsoft.eShopWeb.ApplicationCore.Services;

public class PaymentService : IPaymentService
{
    private static readonly TimeSpan AuthorizationExpirySafetyMargin = TimeSpan.FromHours(1);

    private readonly IRepository<Order> _orderRepository;
    private readonly IRepository<Payment> _paymentRepository;
    private readonly IRepository<SavedCard> _savedCardRepository;
    private readonly IPayPalClient _payPalClient;
    private readonly PayPalSettings _payPalSettings;

    public PaymentService(
        IRepository<Order> orderRepository,
        IRepository<Payment> paymentRepository,
        IRepository<SavedCard> savedCardRepository,
        IPayPalClient payPalClient,
        PayPalSettings payPalSettings)
    {
        _orderRepository = orderRepository;
        _paymentRepository = paymentRepository;
        _savedCardRepository = savedCardRepository;
        _payPalClient = payPalClient;
        _payPalSettings = payPalSettings;
    }

    public async Task<Payment?> PayOrderAsync(string buyerId, int orderId, CardDetails? card, int? savedCardId, CancellationToken cancellationToken = default)
    {
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));
        if (card is null && savedCardId is null)
        {
            throw new PaymentStateException("Either card details or a saved card (paymentMethodId) must be supplied.");
        }

        var order = await _orderRepository.GetByIdAsync(orderId, cancellationToken);
        if (order is null || order.BuyerId != buyerId)
        {
            return null;
        }

        var payment = await _paymentRepository.FirstOrDefaultAsync(new PaymentByOrderIdSpec(orderId), cancellationToken);

        // Idempotency: a repeated pay request for an already-authorized order
        // returns the existing hold instead of creating a second one.
        if (order.Status == OrderStatus.PaymentAuthorized && payment?.AuthorizationId is not null)
        {
            return payment;
        }
        if (order.Status != OrderStatus.PendingPayment)
        {
            throw new PaymentStateException($"Order {orderId} is in status {order.Status} and cannot be paid.");
        }

        string? vaultTokenId = null;
        if (savedCardId is not null)
        {
            var savedCard = await _savedCardRepository.FirstOrDefaultAsync(
                new SavedCardByIdAndBuyerSpec(savedCardId.Value, buyerId), cancellationToken);
            if (savedCard is null)
            {
                throw new PaymentStateException($"Saved card {savedCardId} was not found for this shopper.");
            }
            vaultTokenId = savedCard.VaultTokenId;
        }

        payment ??= new Payment(order.Id, buyerId, order.Total(), _payPalSettings.Currency);

        var payPalOrderId = payment.PayPalOrderId;
        var invoiceId = payment.InvoiceId ?? NewInvoiceId(order.Id);
        if (payPalOrderId is null)
        {
            payPalOrderId = await _payPalClient.CreateOrderAsync(
                payment.OrderTotal, payment.Currency,
                referenceId: order.Id.ToString(),
                invoiceId: invoiceId,
                idempotencyKey: $"eshop-order-{order.Id}-create-{invoiceId}",
                cancellationToken);
        }

        var authorization = vaultTokenId is not null
            ? await _payPalClient.AuthorizeOrderWithVaultedCardAsync(payPalOrderId, vaultTokenId, $"eshop-authorize-{invoiceId}", cancellationToken)
            : await _payPalClient.AuthorizeOrderWithCardAsync(payPalOrderId, card!, $"eshop-authorize-{invoiceId}", cancellationToken);

        if (authorization.Status is "DENIED")
        {
            throw new PaymentDeclinedException($"PayPal denied the authorization for order {orderId}.");
        }

        payment.SetAuthorization(payPalOrderId, invoiceId, authorization.Id, authorization.Status, authorization.Amount, authorization.ExpirationTime);
        order.MarkPaymentAuthorized();

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

    public async Task<Payment?> CaptureOrderPaymentAsync(int orderId, CancellationToken cancellationToken = default)
    {
        var order = await _orderRepository.GetByIdAsync(orderId, cancellationToken);
        if (order is null)
        {
            return null;
        }

        var payment = await _paymentRepository.FirstOrDefaultAsync(new PaymentByOrderIdSpec(orderId), cancellationToken);

        // Idempotency: fulfilling an already-fulfilled order returns the existing capture.
        if (order.Status == OrderStatus.Fulfilled && payment?.CaptureId is not null)
        {
            return payment;
        }
        if (order.Status != OrderStatus.PaymentAuthorized || payment?.AuthorizationId is null)
        {
            throw new PaymentStateException($"Order {orderId} is in status {order.Status} and cannot be fulfilled.");
        }

        var authorization = await _payPalClient.GetAuthorizationAsync(payment.AuthorizationId, cancellationToken);

        // Renew the hold when it has gone stale instead of failing the fulfilment.
        if (authorization.Status is "CREATED" or "PENDING"
            && authorization.ExpirationTime is not null
            && authorization.ExpirationTime.Value - DateTimeOffset.UtcNow < AuthorizationExpirySafetyMargin)
        {
            authorization = await ReauthorizeAsync(payment, authorization, cancellationToken);
        }

        PayPalCaptureInfo capture;
        try
        {
            capture = await _payPalClient.CaptureAuthorizationAsync(
                authorization.Id, payment.OrderTotal, payment.Currency,
                idempotencyKey: $"eshop-capture-{payment.InvoiceId}",
                cancellationToken);
        }
        catch (PayPalApiException ex) when (ex.StatusCode == 422)
        {
            // The hold expired between the check and the capture: renew once, then retry.
            authorization = await ReauthorizeAsync(payment,
                await _payPalClient.GetAuthorizationAsync(payment.AuthorizationId, cancellationToken), cancellationToken);
            capture = await _payPalClient.CaptureAuthorizationAsync(
                authorization.Id, payment.OrderTotal, payment.Currency,
                idempotencyKey: $"eshop-capture-{payment.InvoiceId}",
                cancellationToken);
        }

        if (capture.Status is "DECLINED" or "FAILED")
        {
            throw new PaymentDeclinedException($"PayPal could not capture the payment for order {orderId} (capture status {capture.Status}).");
        }

        payment.SetCapture(capture.Id, capture.Status, capture.GrossAmount, capture.PayPalFee, capture.NetAmount);
        order.MarkFulfilled();

        await _paymentRepository.UpdateAsync(payment, cancellationToken);
        await _orderRepository.UpdateAsync(order, cancellationToken);

        return payment;
    }

    public async Task<Payment?> CancelOrderPaymentAsync(int orderId, CancellationToken cancellationToken = default)
    {
        var order = await _orderRepository.GetByIdAsync(orderId, cancellationToken);
        if (order is null)
        {
            return null;
        }

        var payment = await _paymentRepository.FirstOrDefaultAsync(new PaymentByOrderIdSpec(orderId), cancellationToken);

        // Idempotency: cancelling an already-cancelled order returns current state.
        if (order.Status == OrderStatus.Cancelled)
        {
            return payment;
        }
        if (order.Status == OrderStatus.Fulfilled)
        {
            throw new PaymentStateException($"Order {orderId} has already been fulfilled and its payment captured; issue a refund instead of cancelling.");
        }

        if (payment?.AuthorizationId is not null && payment.AuthorizationStatus is not "VOIDED")
        {
            await _payPalClient.VoidAuthorizationAsync(payment.AuthorizationId, $"eshop-void-{payment.InvoiceId}", cancellationToken);
            payment.UpdateAuthorization(payment.AuthorizationId, "VOIDED", payment.AuthorizedAmount ?? payment.OrderTotal, payment.AuthorizationExpiresAt);
            await _paymentRepository.UpdateAsync(payment, cancellationToken);
        }

        order.MarkCancelled();
        await _orderRepository.UpdateAsync(order, cancellationToken);

        return payment;
    }

    public async Task<PaymentRefund?> RefundOrderPaymentAsync(string buyerId, int orderId, decimal? amount, string idempotencyKey, string? noteToPayer, CancellationToken cancellationToken = default)
    {
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));
        Guard.Against.NullOrEmpty(idempotencyKey, nameof(idempotencyKey));

        // Idempotency: the same caller-supplied key always returns the original refund.
        var existingPayment = await _paymentRepository.FirstOrDefaultAsync(new PaymentByRefundKeySpec(idempotencyKey), cancellationToken);
        var existingRefund = existingPayment?.Refunds.FirstOrDefault(r => r.IdempotencyKey == idempotencyKey);
        if (existingRefund is not null)
        {
            if (existingPayment!.OrderId != orderId || existingPayment.BuyerId != buyerId)
            {
                throw new PaymentStateException("This idempotency key has already been used for a different refund.");
            }
            return existingRefund;
        }

        var order = await _orderRepository.GetByIdAsync(orderId, cancellationToken);
        if (order is null || order.BuyerId != buyerId)
        {
            return null;
        }
        if (order.Status != OrderStatus.Fulfilled)
        {
            throw new PaymentStateException($"Order {orderId} is in status {order.Status}; only fulfilled orders can be refunded.");
        }

        var payment = await _paymentRepository.FirstOrDefaultAsync(new PaymentByOrderIdSpec(orderId), cancellationToken);
        if (payment?.CaptureId is null)
        {
            throw new PaymentStateException($"Order {orderId} has no captured payment to refund.");
        }

        var refundAmount = amount ?? payment.RefundableAmount;
        if (refundAmount <= 0 || refundAmount > payment.RefundableAmount)
        {
            throw new PaymentStateException(
                $"Refund amount {refundAmount:0.00} exceeds the refundable balance {payment.RefundableAmount:0.00} " +
                $"(captured {payment.CapturedAmount:0.00}, already refunded {payment.TotalRefunded:0.00}).");
        }

        // Namespace the caller's key with this payment's unique invoice id so the
        // PayPal-Request-Id cannot collide with unrelated runs on a shared account.
        var refund = await _payPalClient.RefundCaptureAsync(
            payment.CaptureId, refundAmount, payment.Currency, noteToPayer,
            $"eshop-refund-{payment.InvoiceId}-{idempotencyKey}", cancellationToken);

        var entity = payment.AddRefund(refund.Id, idempotencyKey, refund.Amount, refund.Status, noteToPayer);
        await _paymentRepository.UpdateAsync(payment, cancellationToken);

        return entity;
    }

    public async Task<SavedCard> SaveCardAsync(string buyerId, CardDetails card, CancellationToken cancellationToken = default)
    {
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));
        Guard.Against.Null(card, nameof(card));

        var token = await _payPalClient.CreatePaymentTokenAsync(
            card, merchantCustomerId: buyerId,
            idempotencyKey: $"eshop-vault-{buyerId}-{Guid.NewGuid():N}",
            cancellationToken);

        var savedCard = new SavedCard(buyerId, token.TokenId, token.Brand, token.Last4, token.Expiry, token.CardholderName);
        return await _savedCardRepository.AddAsync(savedCard, cancellationToken);
    }

    public async Task<IReadOnlyList<SavedCard>> ListSavedCardsAsync(string buyerId, CancellationToken cancellationToken = default)
    {
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));
        return await _savedCardRepository.ListAsync(new SavedCardsByBuyerSpec(buyerId), cancellationToken);
    }

    public async Task<bool> DeleteSavedCardAsync(string buyerId, int savedCardId, CancellationToken cancellationToken = default)
    {
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));

        var savedCard = await _savedCardRepository.FirstOrDefaultAsync(
            new SavedCardByIdAndBuyerSpec(savedCardId, buyerId), cancellationToken);
        if (savedCard is null)
        {
            return false;
        }

        try
        {
            await _payPalClient.DeletePaymentTokenAsync(savedCard.VaultTokenId, cancellationToken);
        }
        catch (PayPalApiException ex) when (ex.StatusCode == 404)
        {
            // Already gone from PayPal's vault; still remove the local reference.
        }

        await _savedCardRepository.DeleteAsync(savedCard, cancellationToken);
        return true;
    }

    public async Task<ReconciliationReport> ReconcileAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken = default)
    {
        if (to <= from)
        {
            throw new PaymentStateException("The 'to' date-time must be after the 'from' date-time.");
        }

        var transactions = await _payPalClient.ListTransactionsAsync(from, to, cancellationToken);
        var payments = await _paymentRepository.ListAsync(new PaymentsCreatedBetweenSpec(from, to), cancellationToken);

        var entries = new List<ReconciliationEntry>();
        var matchedPaymentOrderIds = new HashSet<int>();

        foreach (var transaction in transactions)
        {
            int? matchedOrderId = null;
            string? matchedEntity = null;

            foreach (var payment in payments)
            {
                if (payment.CaptureId is not null && payment.CaptureId == transaction.TransactionId)
                {
                    (matchedOrderId, matchedEntity) = (payment.OrderId, "capture");
                    break;
                }
                if (payment.AuthorizationId is not null && payment.AuthorizationId == transaction.TransactionId)
                {
                    (matchedOrderId, matchedEntity) = (payment.OrderId, "authorization");
                    break;
                }
                if (payment.Refunds.Any(r => r.PayPalRefundId == transaction.TransactionId))
                {
                    (matchedOrderId, matchedEntity) = (payment.OrderId, "refund");
                    break;
                }
                if (transaction.InvoiceId is not null && payment.InvoiceId is not null && transaction.InvoiceId == payment.InvoiceId)
                {
                    (matchedOrderId, matchedEntity) = (payment.OrderId, "invoice");
                    break;
                }
            }

            if (matchedOrderId is not null)
            {
                matchedPaymentOrderIds.Add(matchedOrderId.Value);
            }
            entries.Add(new ReconciliationEntry(transaction, matchedOrderId, matchedEntity));
        }

        var missingFromPayPal = payments
            .Where(p => p.CaptureId is not null && !matchedPaymentOrderIds.Contains(p.OrderId)
                && !transactions.Any(t => t.TransactionId == p.CaptureId))
            .Select(p => new UnmatchedPaymentInfo(
                p.OrderId, p.PayPalOrderId, p.AuthorizationId, p.CaptureId,
                p.Refunds.Select(r => r.PayPalRefundId).ToList()))
            .ToList();

        var missingFromEShop = entries
            .Where(e => e.MatchedOrderId is null)
            .Select(e => e.Transaction)
            .ToList();

        return new ReconciliationReport(from, to, entries, missingFromPayPal, missingFromEShop);
    }

    private async Task<PayPalAuthorizationInfo> ReauthorizeAsync(Payment payment, PayPalAuthorizationInfo authorization, CancellationToken cancellationToken)
    {
        try
        {
            var renewed = await _payPalClient.ReauthorizeAsync(
                authorization.Id, payment.OrderTotal, payment.Currency,
                idempotencyKey: $"eshop-reauthorize-{payment.InvoiceId}-{Guid.NewGuid():N}",
                cancellationToken);
            payment.UpdateAuthorization(renewed.Id, renewed.Status, renewed.Amount, renewed.ExpirationTime);
            await _paymentRepository.UpdateAsync(payment, cancellationToken);
            return renewed;
        }
        catch (PayPalApiException ex)
        {
            throw new AuthorizationNotRenewableException(
                $"The PayPal authorization {authorization.Id} for order {payment.OrderId} can no longer be renewed " +
                $"(PayPal: {ex.Message}). Authorizations can only be reauthorized within 29 days of the original hold. " +
                $"Cancel this order and ask the shopper to place it again so a fresh hold can be taken.");
        }
    }

    private static string NewInvoiceId(int orderId) => $"eshop-order-{orderId}-{Guid.NewGuid().ToString("N")[..8]}";
}
