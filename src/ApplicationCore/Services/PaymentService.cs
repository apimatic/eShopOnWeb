using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Specifications;

namespace Microsoft.eShopWeb.ApplicationCore.Services;

public class PaymentService : IPaymentService
{
    // PayPal-Request-Id values must be unique per distinct request for the merchant
    // account. Namespacing them per app run keeps double-clicks idempotent (same id within
    // the run) without ever colliding with requests made by a previous run.
    private static readonly string RunId = Guid.NewGuid().ToString("N")[..8];

    private readonly IRepository<Order> _orderRepository;
    private readonly IRepository<Payment> _paymentRepository;
    private readonly IRepository<SavedCard> _savedCardRepository;
    private readonly IPayPalClient _payPalClient;
    private readonly PayPalSettings _settings;
    private readonly IAppLogger<PaymentService> _logger;

    public PaymentService(
        IRepository<Order> orderRepository,
        IRepository<Payment> paymentRepository,
        IRepository<SavedCard> savedCardRepository,
        IPayPalClient payPalClient,
        PayPalSettings settings,
        IAppLogger<PaymentService> logger)
    {
        _orderRepository = orderRepository;
        _paymentRepository = paymentRepository;
        _savedCardRepository = savedCardRepository;
        _payPalClient = payPalClient;
        _settings = settings;
        _logger = logger;
    }

    public async Task<Payment> PayOrderAsync(int orderId, string buyerId, CardDetails? card, int? savedCardId, CancellationToken cancellationToken = default)
    {
        var order = await _orderRepository.FirstOrDefaultAsync(new OrderWithItemsByIdSpec(orderId), cancellationToken);
        if (order == null || order.BuyerId != buyerId)
        {
            throw new PaymentException(404, $"Order {orderId} was not found.");
        }

        var payment = await _paymentRepository.FirstOrDefaultAsync(new PaymentByOrderIdSpec(orderId), cancellationToken);

        // Idempotency: a repeat pay call on an already-authorized or already-captured order
        // returns the existing hold instead of authorizing the shopper again.
        if (payment != null && (payment.HasActiveAuthorization || payment.IsCaptured))
        {
            return payment;
        }

        if (order.Status == OrderStatus.Cancelled)
        {
            throw new PaymentException(409, $"Order {orderId} is cancelled and can no longer be paid.");
        }

        var (source, label) = await BuildPaymentSourceAsync(buyerId, card, savedCardId, cancellationToken);

        var total = order.Total();
        // invoice_id must be unique per PayPal order for the merchant; the eShop order id
        // alone can collide (e.g. a fresh database reusing ids), so suffix it uniquely.
        // The stable order id still travels as reference_id/custom_id for reconciliation.
        var invoiceId = $"eshop-{RunId}-order-{order.Id}";
        PayPalOrderInfo payPalOrder;
        try
        {
            payPalOrder = await _payPalClient.CreateOrderAsync(total, _settings.Currency, order.Id.ToString(), invoiceId, source,
                $"eshop-{RunId}-order-{order.Id}-create", cancellationToken);
        }
        catch (PayPalApiException ex)
        {
            throw new PaymentException(422, $"PayPal could not create the payment for order {order.Id}: {ex.Message}");
        }

        if (payPalOrder.Status == "PAYER_ACTION_REQUIRED")
        {
            throw new PaymentException(422,
                "PayPal requires the shopper to approve this payment in a browser (a 3-D Secure challenge). " +
                "This integration does not support an approval round-trip.");
        }

        if (payment == null)
        {
            payment = new Payment(order.Id, buyerId, total, _settings.Currency, payPalOrder.Id, invoiceId, label);
            await _paymentRepository.AddAsync(payment, cancellationToken);
        }
        else
        {
            payment.ResetForRetry(payPalOrder.Id, invoiceId, label);
            await _paymentRepository.UpdateAsync(payment, cancellationToken);
        }

        // With a card payment source PayPal usually authorizes at order creation; only call
        // the authorize endpoint when the create response did not carry an authorization.
        PayPalAuthorizationInfo authorization;
        try
        {
            authorization = payPalOrder.Authorization
                ?? await _payPalClient.AuthorizeOrderAsync(payPalOrder.Id, $"eshop-{RunId}-order-{order.Id}-authorize", cancellationToken);
        }
        catch (PayPalApiException ex)
        {
            throw new PaymentException(422, $"PayPal could not authorize the payment for order {order.Id}: {ex.Message}");
        }

        if (authorization.Status == "DENIED")
        {
            payment.RecordAuthorization(authorization.Id, authorization.Status, authorization.ExpirationTime);
            await _paymentRepository.UpdateAsync(payment, cancellationToken);
            throw new PaymentException(422, "PayPal declined the card for this payment. Use a different card or payment method.");
        }

        payment.RecordAuthorization(authorization.Id, authorization.Status, authorization.ExpirationTime);
        order.MarkPaymentAuthorized();

        await _orderRepository.UpdateAsync(order, cancellationToken);
        await _paymentRepository.UpdateAsync(payment, cancellationToken);

        _logger.LogInformation("Order {OrderId} authorized at PayPal: authorization {AuthorizationId}, amount {Amount} {Currency}.",
            order.Id, authorization.Id, total, _settings.Currency);

        return payment;
    }

    public async Task<Payment> FulfilOrderAsync(int orderId, CancellationToken cancellationToken = default)
    {
        var order = await _orderRepository.FirstOrDefaultAsync(new OrderWithItemsByIdSpec(orderId), cancellationToken);
        if (order == null)
        {
            throw new PaymentException(404, $"Order {orderId} was not found.");
        }

        var payment = await _paymentRepository.FirstOrDefaultAsync(new PaymentByOrderIdSpec(orderId), cancellationToken);

        // Idempotency: fulfilment of an already-captured order returns the existing capture.
        if (payment != null && payment.IsCaptured)
        {
            return payment;
        }

        if (order.Status == OrderStatus.Cancelled)
        {
            throw new PaymentException(409, $"Order {orderId} is cancelled and cannot be fulfilled.");
        }

        if (payment == null || payment.AuthorizationId == null)
        {
            throw new PaymentException(409, $"Order {orderId} has no payment authorization. The shopper must pay before the order can be fulfilled.");
        }

        var authorization = await _payPalClient.GetAuthorizationAsync(payment.AuthorizationId, cancellationToken);
        payment.RecordAuthorizationStatus(authorization.Status);

        var stale = authorization.ExpirationTime.HasValue && authorization.ExpirationTime.Value <= DateTimeOffset.UtcNow;
        var capturable = authorization.Status is "CREATED" or "PENDING" or "PARTIALLY_CAPTURED";

        if (!capturable || stale)
        {
            await RenewAuthorizationAsync(orderId, payment, cancellationToken);
        }

        try
        {
            await CaptureAsync(orderId, payment, cancellationToken);
        }
        catch (PayPalApiException ex) when (ex.HttpStatusCode == 422 && ex.Issue != null && ex.Issue.Contains("EXPIRED"))
        {
            // The hold went stale between our check and the capture: renew once and retry.
            _logger.LogWarning("Capture for order {OrderId} rejected ({Issue}); attempting one reauthorization.", orderId, ex.Issue);
            await RenewAuthorizationAsync(orderId, payment, cancellationToken);
            await CaptureAsync(orderId, payment, cancellationToken);
        }
        catch (PayPalApiException ex)
        {
            throw new PaymentException(422,
                $"PayPal could not capture the authorized funds for order {orderId} ({ex.Issue ?? ex.ErrorName ?? ex.Message}). " +
                "The shopper's money is still on hold; retry fulfilment, or cancel the order to release the hold.");
        }

        order.MarkFulfilled();
        await _orderRepository.UpdateAsync(order, cancellationToken);
        await _paymentRepository.UpdateAsync(payment, cancellationToken);

        _logger.LogInformation("Order {OrderId} fulfilled: capture {CaptureId}, gross {Gross} {Currency}, fee {Fee}, net {Net}.",
            order.Id, payment.CaptureId ?? string.Empty, payment.CapturedAmount ?? 0m, payment.Currency, payment.PayPalFee ?? 0m, payment.NetAmount ?? 0m);

        return payment;
    }

    public async Task<Payment?> CancelOrderAsync(int orderId, CancellationToken cancellationToken = default)
    {
        var order = await _orderRepository.FirstOrDefaultAsync(new OrderWithItemsByIdSpec(orderId), cancellationToken);
        if (order == null)
        {
            throw new PaymentException(404, $"Order {orderId} was not found.");
        }

        var payment = await _paymentRepository.FirstOrDefaultAsync(new PaymentByOrderIdSpec(orderId), cancellationToken);

        if (order.Status == OrderStatus.Cancelled)
        {
            return payment;
        }

        if (order.Status is OrderStatus.Fulfilled or OrderStatus.PartiallyRefunded or OrderStatus.Refunded)
        {
            throw new PaymentException(409, $"Order {orderId} is already fulfilled and cannot be cancelled; issue a refund instead.");
        }

        if (payment != null && payment.HasActiveAuthorization)
        {
            var voided = await _payPalClient.VoidAuthorizationAsync(payment.AuthorizationId!, $"eshop-{RunId}-order-{orderId}-void", cancellationToken);
            payment.RecordAuthorizationStatus(voided.Status);
            await _paymentRepository.UpdateAsync(payment, cancellationToken);
        }

        order.MarkCancelled();
        await _orderRepository.UpdateAsync(order, cancellationToken);

        _logger.LogInformation("Order {OrderId} cancelled; held funds released.", orderId);

        return payment;
    }

    public async Task<PaymentRefund> RefundOrderAsync(int orderId, string buyerId, decimal? amount, string idempotencyKey, string? note, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(idempotencyKey))
        {
            throw new PaymentException(400, "An idempotency key is required for refunds.");
        }

        var order = await _orderRepository.FirstOrDefaultAsync(new OrderWithItemsByIdSpec(orderId), cancellationToken);
        if (order == null || order.BuyerId != buyerId)
        {
            throw new PaymentException(404, $"Order {orderId} was not found.");
        }

        var payment = await _paymentRepository.FirstOrDefaultAsync(new PaymentByOrderIdSpec(orderId), cancellationToken);

        // Idempotency: a repeated request under the same key returns the original refund.
        var existing = payment?.Refunds.FirstOrDefault(r => r.IdempotencyKey == idempotencyKey);
        if (existing != null)
        {
            return existing;
        }

        if (payment == null || !payment.IsCaptured || payment.CaptureId == null)
        {
            throw new PaymentException(409, $"Order {orderId} has no captured payment to refund.");
        }

        var refundable = payment.RefundableAmount;
        var refundAmount = amount ?? refundable;
        refundAmount = Math.Round(refundAmount, 2);

        if (refundAmount <= 0)
        {
            throw new PaymentException(400, "The refund amount must be greater than zero.");
        }

        if (refundAmount > refundable)
        {
            throw new PaymentException(409,
                $"Order {orderId} cannot be refunded by {refundAmount:0.00} {payment.Currency}: only {refundable:0.00} {payment.Currency} of the captured amount remains refundable.");
        }

        var refundInfo = await _payPalClient.RefundCaptureAsync(payment.CaptureId, refundAmount, payment.Currency,
            order.Id.ToString(), note, $"eshop-{RunId}-refund-{orderId}-{idempotencyKey}", cancellationToken);

        var refund = payment.AddRefund(refundInfo.Id, refundInfo.Amount > 0 ? refundInfo.Amount : refundAmount, refundInfo.Status, idempotencyKey, note);

        var fullyRefunded = payment.RefundableAmount <= 0m;
        payment.RecordCaptureStatus(fullyRefunded ? "REFUNDED" : "PARTIALLY_REFUNDED");
        order.MarkRefunded(fullyRefunded);

        await _paymentRepository.UpdateAsync(payment, cancellationToken);
        await _orderRepository.UpdateAsync(order, cancellationToken);

        _logger.LogInformation("Order {OrderId} refunded {Amount} {Currency}: refund {RefundId}.",
            order.Id, refund.Amount, refund.Currency, refund.PayPalRefundId);

        return refund;
    }

    public async Task<SavedCard> SaveCardAsync(string buyerId, CardDetails card, CancellationToken cancellationToken = default)
    {
        var vaulted = await _payPalClient.CreateVaultPaymentTokenAsync(card, buyerId,
            $"eshop-{RunId}-vault-{Guid.NewGuid():N}", cancellationToken);

        var savedCard = new SavedCard(buyerId, vaulted.VaultTokenId, vaulted.Brand, vaulted.LastDigits, vaulted.Expiry, vaulted.CardholderName);
        await _savedCardRepository.AddAsync(savedCard, cancellationToken);

        _logger.LogInformation("Saved card {SavedCardId} (vault token {VaultTokenId}) for shopper.", savedCard.Id, vaulted.VaultTokenId);

        return savedCard;
    }

    public async Task<IReadOnlyList<SavedCard>> ListSavedCardsAsync(string buyerId, CancellationToken cancellationToken = default)
    {
        return await _savedCardRepository.ListAsync(new SavedCardsByBuyerSpec(buyerId), cancellationToken);
    }

    public async Task DeleteSavedCardAsync(int savedCardId, string buyerId, CancellationToken cancellationToken = default)
    {
        var savedCard = await _savedCardRepository.GetByIdAsync(savedCardId, cancellationToken);
        if (savedCard == null || savedCard.BuyerId != buyerId)
        {
            throw new PaymentException(404, $"Payment method {savedCardId} was not found.");
        }

        try
        {
            await _payPalClient.DeleteVaultPaymentTokenAsync(savedCard.VaultTokenId, cancellationToken);
        }
        catch (PayPalApiException ex) when (ex.HttpStatusCode == 404)
        {
            // Already gone from PayPal's vault; still remove the local reference.
        }

        await _savedCardRepository.DeleteAsync(savedCard, cancellationToken);
    }

    public async Task<ReconciliationResult> ReconcileAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken = default)
    {
        var transactions = await _payPalClient.SearchTransactionsAsync(from, to, cancellationToken);
        var payments = await _paymentRepository.ListAsync(new PaymentsInDateRangeSpec(from, to), cancellationToken);

        var result = new ReconciliationResult { From = from, To = to };

        foreach (var txn in transactions)
        {
            var entry = new ReconciliationTransaction
            {
                TransactionId = txn.TransactionId,
                ReferenceId = txn.ReferenceId,
                EventCode = txn.EventCode,
                Status = txn.Status,
                Amount = txn.Amount,
                Currency = txn.Currency,
                Fee = txn.Fee,
                Time = txn.InitiationTime,
                InvoiceId = txn.InvoiceId
            };

            var match = payments.FirstOrDefault(p =>
                p.PayPalOrderId == txn.ReferenceId ||
                p.AuthorizationId == txn.TransactionId ||
                p.CaptureId == txn.TransactionId ||
                p.Refunds.Any(r => r.PayPalRefundId == txn.TransactionId) ||
                (txn.InvoiceId != null && txn.InvoiceId == p.OrderId.ToString()) ||
                (txn.CustomField != null && txn.CustomField == p.OrderId.ToString()));

            if (match != null)
            {
                entry.Match = "matched";
                entry.MatchedOrderId = match.OrderId;
            }

            result.Transactions.Add(entry);
        }

        var payPalIds = new HashSet<string>(transactions
            .SelectMany(t => new[] { t.TransactionId, t.ReferenceId })
            .Where(id => !string.IsNullOrEmpty(id))!);

        foreach (var payment in payments)
        {
            var seenByPayPal = payPalIds.Contains(payment.PayPalOrderId)
                || (payment.AuthorizationId != null && payPalIds.Contains(payment.AuthorizationId))
                || (payment.CaptureId != null && payPalIds.Contains(payment.CaptureId))
                || payment.Refunds.Any(r => payPalIds.Contains(r.PayPalRefundId));

            if (!seenByPayPal)
            {
                result.LocalPaymentsNotInPayPal.Add(new ReconciliationLocalPayment
                {
                    OrderId = payment.OrderId,
                    PayPalOrderId = payment.PayPalOrderId,
                    AuthorizationId = payment.AuthorizationId,
                    CaptureId = payment.CaptureId,
                    Amount = payment.Amount,
                    Currency = payment.Currency,
                    CreatedAt = payment.CreatedAt
                });
            }
        }

        return result;
    }

    private async Task<(PayPalPaymentSource Source, string Label)> BuildPaymentSourceAsync(string buyerId, CardDetails? card, int? savedCardId, CancellationToken cancellationToken)
    {
        if (savedCardId.HasValue)
        {
            var savedCard = await _savedCardRepository.GetByIdAsync(savedCardId.Value, cancellationToken);
            if (savedCard == null || savedCard.BuyerId != buyerId)
            {
                throw new PaymentException(404, $"Payment method {savedCardId} was not found.");
            }

            return (new PayPalPaymentSource { VaultTokenId = savedCard.VaultTokenId },
                $"{savedCard.Brand ?? "Card"} ****{savedCard.LastDigits}");
        }

        if (card == null || string.IsNullOrWhiteSpace(card.Number) || string.IsNullOrWhiteSpace(card.Expiry))
        {
            throw new PaymentException(400, "Provide either card details or a saved paymentMethodId to pay the order.");
        }

        var lastDigits = card.Number.Length >= 4 ? card.Number[^4..] : card.Number;
        return (new PayPalPaymentSource { Card = card }, $"Card ****{lastDigits}");
    }

    private async Task RenewAuthorizationAsync(int orderId, Payment payment, CancellationToken cancellationToken)
    {
        try
        {
            var renewed = await _payPalClient.ReauthorizeAsync(payment.AuthorizationId!, payment.Amount, payment.Currency,
                $"eshop-{RunId}-order-{orderId}-reauthorize-{DateTimeOffset.UtcNow.ToUnixTimeSeconds()}", cancellationToken);
            payment.RecordAuthorization(renewed.Id, renewed.Status, renewed.ExpirationTime);
            await _paymentRepository.UpdateAsync(payment, cancellationToken);
        }
        catch (PayPalApiException ex)
        {
            throw new PaymentException(422,
                $"The authorization for order {orderId} has expired and PayPal could not renew it ({ex.Issue ?? ex.ErrorName ?? ex.Message}). " +
                "Do not fulfil this order against the old hold; ask the shopper to pay again, then fulfil.");
        }
    }

    private async Task CaptureAsync(int orderId, Payment payment, CancellationToken cancellationToken)
    {
        var capture = await _payPalClient.CaptureAuthorizationAsync(payment.AuthorizationId!, payment.Amount, payment.Currency,
            payment.InvoiceId, $"eshop-{RunId}-order-{orderId}-capture", cancellationToken);

        if (capture.Status == "DECLINED" || capture.Status == "FAILED")
        {
            payment.RecordCapture(capture.Id, capture.Status, capture.Amount, capture.PayPalFee, capture.NetAmount);
            await _paymentRepository.UpdateAsync(payment, cancellationToken);
            throw new PaymentException(422,
                $"PayPal declined the capture for order {orderId}. The shopper's funds were not taken; ask the shopper to pay again.");
        }

        payment.RecordCapture(capture.Id, capture.Status, capture.Amount, capture.PayPalFee, capture.NetAmount);
    }
}
