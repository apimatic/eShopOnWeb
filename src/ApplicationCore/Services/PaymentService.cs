using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Payments;
using Microsoft.eShopWeb.ApplicationCore.Specifications;

namespace Microsoft.eShopWeb.ApplicationCore.Services;

/// <summary>
/// Orchestrates the order payment lifecycle against the payment gateway.
/// Money movement: authorize at pay time (hold), capture at fulfilment, void on cancel,
/// refund after fulfilment. Idempotency in effect comes from local payment state plus
/// deterministic provider request ids.
/// </summary>
public class PaymentService : IPaymentService
{
    /// <summary>How long an in-flight authorization attempt is trusted before a retry is allowed.</summary>
    private static readonly TimeSpan AuthorizingStaleAfter = TimeSpan.FromMinutes(5);

    private readonly IPaymentGateway _gateway;
    private readonly IRepository<Order> _orderRepository;
    private readonly IRepository<Payment> _paymentRepository;
    private readonly IRepository<SavedCard> _savedCardRepository;
    private readonly IRepository<CatalogItem> _catalogRepository;
    private readonly IUriComposer _uriComposer;
    private readonly IAppLogger<PaymentService> _logger;

    public PaymentService(IPaymentGateway gateway,
        IRepository<Order> orderRepository,
        IRepository<Payment> paymentRepository,
        IRepository<SavedCard> savedCardRepository,
        IRepository<CatalogItem> catalogRepository,
        IUriComposer uriComposer,
        IAppLogger<PaymentService> logger)
    {
        _gateway = gateway;
        _orderRepository = orderRepository;
        _paymentRepository = paymentRepository;
        _savedCardRepository = savedCardRepository;
        _catalogRepository = catalogRepository;
        _uriComposer = uriComposer;
        _logger = logger;
    }

    public async Task<PlaceOrderResult> PlaceOrderAsync(string buyerId, PlaceOrderInput input,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(buyerId))
        {
            return new PlaceOrderResult { Error = new PaymentError(PaymentErrorType.Validation, "No authenticated buyer.") };
        }

        if (input.Items == null || input.Items.Count == 0)
        {
            return new PlaceOrderResult { Error = new PaymentError(PaymentErrorType.Validation, "At least one order item is required.") };
        }

        if (input.Items.Any(i => i.Quantity <= 0))
        {
            return new PlaceOrderResult { Error = new PaymentError(PaymentErrorType.Validation, "Item quantities must be positive.") };
        }

        var address = input.ShipToAddress;
        if (address == null || string.IsNullOrWhiteSpace(address.Street) || string.IsNullOrWhiteSpace(address.City) ||
            string.IsNullOrWhiteSpace(address.Country) || string.IsNullOrWhiteSpace(address.ZipCode))
        {
            return new PlaceOrderResult { Error = new PaymentError(PaymentErrorType.Validation, "A complete shipping address (street, city, country, zip code) is required.") };
        }

        var requestedIds = input.Items.Select(i => i.CatalogItemId).Distinct().ToArray();
        var catalogItems = await _catalogRepository.ListAsync(new CatalogItemsSpecification(requestedIds));
        if (catalogItems.Count != requestedIds.Length)
        {
            var knownIds = catalogItems.Select(c => c.Id).ToHashSet();
            var missing = requestedIds.Where(id => !knownIds.Contains(id));
            return new PlaceOrderResult { Error = new PaymentError(PaymentErrorType.Validation, $"Unknown catalog item(s): {string.Join(", ", missing)}.") };
        }

        var orderItems = input.Items.Select(item =>
        {
            var catalogItem = catalogItems.First(c => c.Id == item.CatalogItemId);
            var itemOrdered = new CatalogItemOrdered(catalogItem.Id, catalogItem.Name,
                _uriComposer.ComposePicUri(catalogItem.PictureUri));
            return new OrderItem(itemOrdered, catalogItem.Price, item.Quantity);
        }).ToList();

        var order = new Order(buyerId,
            new Address(address.Street, address.City, address.State, address.Country, address.ZipCode),
            orderItems);

        var added = await _orderRepository.AddAsync(order);

        return new PlaceOrderResult { Succeeded = true, OrderId = added.Id, Total = added.Total() };
    }

    public async Task<PayOrderResult> PayOrderAsync(string buyerId, int orderId, int? paymentMethodId, CardInput? card,
        CancellationToken ct = default)
    {
        var order = await _orderRepository.FirstOrDefaultAsync(new OrderWithItemsByIdSpec(orderId));
        if (order == null || order.BuyerId != buyerId)
        {
            return new PayOrderResult { Error = new PaymentError(PaymentErrorType.NotFound, $"Order {orderId} was not found.") };
        }

        var payment = await _paymentRepository.FirstOrDefaultAsync(new PaymentByOrderIdSpecification(orderId));

        // Idempotency in effect: an already-authorized order is answered as-is.
        if (payment != null && payment.Status == PaymentStatus.Authorized)
        {
            return new PayOrderResult { Succeeded = true, Payment = payment };
        }

        if (payment != null && payment.Status is PaymentStatus.Captured or PaymentStatus.Refunded
            or PaymentStatus.PartiallyRefunded or PaymentStatus.Voided or PaymentStatus.Cancelled)
        {
            return new PayOrderResult { Error = new PaymentError(PaymentErrorType.Conflict,
                $"Order {orderId} cannot be paid because its payment is {payment.Status}.") };
        }

        // An in-flight attempt has an unknown outcome: refuse until it settles or goes stale.
        if (payment != null && payment.Status == PaymentStatus.Authorizing &&
            payment.UpdatedAt > DateTimeOffset.UtcNow - AuthorizingStaleAfter)
        {
            return new PayOrderResult { Error = new PaymentError(PaymentErrorType.Conflict,
                "A payment attempt is already in progress for this order. Wait a moment and try again.") };
        }

        var vaultTokenId = (string?)null;
        CardInput? resolvedCard = null;
        if (paymentMethodId.HasValue)
        {
            if (card != null)
            {
                return new PayOrderResult { Error = new PaymentError(PaymentErrorType.Validation,
                    "Provide either paymentMethodId or card details, not both.") };
            }

            var saved = await _savedCardRepository.GetByIdAsync(paymentMethodId.Value);
            if (saved == null || saved.BuyerId != buyerId)
            {
                return new PayOrderResult { Error = new PaymentError(PaymentErrorType.NotFound,
                    $"Payment method {paymentMethodId.Value} was not found.") };
            }

            vaultTokenId = saved.VaultTokenId;
        }
        else if (card != null)
        {
            resolvedCard = card;
        }
        else
        {
            return new PayOrderResult { Error = new PaymentError(PaymentErrorType.Validation,
                "Provide card details or a saved paymentMethodId.") };
        }

        var amount = order.Total();
        var currency = _gateway.Currency;

        if (payment == null)
        {
            payment = Payment.CreateFirstAttempt(orderId, buyerId, amount, currency);
            await _paymentRepository.AddAsync(payment);
        }
        else
        {
            payment.StartNewAttempt();
            await _paymentRepository.UpdateAsync(payment);
        }

        var requestId = $"eshop-auth-{payment.PaymentKey}-{payment.AttemptCount}";

        // A unique invoice id scoped to this payment: PayPal enforces merchant-wide invoice
        // id uniqueness, and the transaction search links provider records back by it.
        var invoiceId = $"eshop-{payment.PaymentKey}";

        var result = vaultTokenId != null
            ? await _gateway.AuthorizeWithVaultTokenAsync(requestId, amount, currency, vaultTokenId, invoiceId, ct)
            : await _gateway.AuthorizeAsync(requestId, amount, currency, resolvedCard!, invoiceId, ct);

        if (!result.Succeeded || result.Value == null)
        {
            payment.MarkAttemptFailed();
            await _paymentRepository.UpdateAsync(payment);
            return new PayOrderResult { Error = result.Error ?? new PaymentError(PaymentErrorType.ProviderError, "The payment could not be authorized.") };
        }

        payment.MarkAuthorized(result.Value.PayPalOrderId, result.Value.AuthorizationId,
            result.Value.AuthorizationStatus, result.Value.ExpiresAt, paymentMethodId);
        await _paymentRepository.UpdateAsync(payment);

        return new PayOrderResult { Succeeded = true, Payment = payment };
    }

    public async Task<OperatorActionResult> FulfilOrderAsync(int orderId, CancellationToken ct = default)
    {
        var payment = await _paymentRepository.FirstOrDefaultAsync(new PaymentByOrderIdSpecification(orderId));
        if (payment == null)
        {
            return new OperatorActionResult { Error = new PaymentError(PaymentErrorType.Conflict,
                $"Order {orderId} has no payment: authorize it before fulfilment.") };
        }

        // Idempotency in effect: an already-captured order is answered as-is.
        if (payment.Status is PaymentStatus.Captured or PaymentStatus.Refunded or PaymentStatus.PartiallyRefunded)
        {
            return new OperatorActionResult { Succeeded = true, Payment = payment };
        }

        if (payment.Status is not PaymentStatus.Authorized)
        {
            return new OperatorActionResult { Error = new PaymentError(PaymentErrorType.Conflict,
                $"Order {orderId} cannot be fulfilled while its payment is {payment.Status}.") };
        }

        var captureError = await CaptureAsync(payment, orderId, ct);
        if (captureError != null)
        {
            return new OperatorActionResult { Error = captureError };
        }

        await _paymentRepository.UpdateAsync(payment);
        return new OperatorActionResult { Succeeded = true, Payment = payment };
    }

    private async Task<PaymentError?> CaptureAsync(Payment payment, int orderId, CancellationToken ct)
    {
        var amount = payment.Amount;
        var currency = payment.Currency;

        // Capture; if the authorization went stale, renew it once and retry. The request id
        // is scoped to this payment's unique key, so it never collides across orders or restarts.
        var capture = await _gateway.CaptureAsync($"eshop-capture-{payment.PaymentKey}",
            payment.PayPalAuthorizationId!, amount, currency, ct);
        if (capture.Succeeded)
        {
            ApplyCapture(payment, capture.Value!);
            return null;
        }

        if (capture.Error?.Type != PaymentErrorType.StaleAuthorization)
        {
            return capture.Error ?? new PaymentError(PaymentErrorType.ProviderError, "The capture failed.");
        }

        var reauthorized = await RenewAuthorizationAsync(payment, orderId, ct);
        if (reauthorized != null)
        {
            return reauthorized;
        }

        capture = await _gateway.CaptureAsync($"eshop-capture-{payment.PaymentKey}",
            payment.PayPalAuthorizationId!, amount, currency, ct);
        if (capture.Succeeded)
        {
            ApplyCapture(payment, capture.Value!);
            return null;
        }

        return capture.Error ?? new PaymentError(PaymentErrorType.ProviderError, "The capture failed.");
    }

    private async Task<PaymentError?> RenewAuthorizationAsync(Payment payment, int orderId, CancellationToken ct)
    {
        var reauthorization = await _gateway.ReauthorizeAsync($"eshop-reauth-{payment.PaymentKey}",
            payment.PayPalAuthorizationId!, payment.Amount, payment.Currency, ct);
        if (!reauthorization.Succeeded || reauthorization.Value == null)
        {
            var reason = reauthorization.Error?.Message ?? "unknown reason";
            return new PaymentError(PaymentErrorType.StaleAuthorization,
                $"The payment authorization for order {orderId} expired and could not be renewed ({reason}). " +
                "Cancel the order to release any hold, or ask the shopper to pay again with a valid payment method.");
        }

        payment.ReplaceAuthorization(reauthorization.Value.AuthorizationId, reauthorization.Value.AuthorizationStatus,
            reauthorization.Value.ExpiresAt);
        await _paymentRepository.UpdateAsync(payment);
        return null;
    }

    private static void ApplyCapture(Payment payment, CaptureOutcome capture)
    {
        payment.MarkCaptured(capture.CaptureId, capture.CaptureStatus, capture.CapturedAmount,
            capture.PayPalFee, capture.NetAmount, "CAPTURED");
    }

    public async Task<OperatorActionResult> CancelOrderAsync(int orderId, CancellationToken ct = default)
    {
        var order = await _orderRepository.GetByIdAsync(orderId);
        if (order == null)
        {
            return new OperatorActionResult { Error = new PaymentError(PaymentErrorType.NotFound, $"Order {orderId} was not found.") };
        }

        var payment = await _paymentRepository.FirstOrDefaultAsync(new PaymentByOrderIdSpecification(orderId));

        // Idempotency in effect: a cancelled order stays cancelled.
        if (payment != null && (payment.Status == PaymentStatus.Voided || payment.Status == PaymentStatus.Cancelled))
        {
            return new OperatorActionResult { Succeeded = true, Payment = payment };
        }

        if (payment != null && payment.Status is PaymentStatus.Captured or PaymentStatus.Refunded
            or PaymentStatus.PartiallyRefunded)
        {
            return new OperatorActionResult { Error = new PaymentError(PaymentErrorType.Conflict,
                $"Order {orderId} was already fulfilled; refund it instead of cancelling.") };
        }

        if (payment != null && payment.Status == PaymentStatus.Authorized)
        {
            var voided = await _gateway.VoidAsync($"eshop-void-{payment.PaymentKey}", payment.PayPalAuthorizationId!, ct);
            if (!voided.Succeeded)
            {
                return new OperatorActionResult { Error = voided.Error ?? new PaymentError(PaymentErrorType.ProviderError, "The hold could not be released.") };
            }

            payment.MarkVoided(voided.Value ?? "VOIDED");
            await _paymentRepository.UpdateAsync(payment);
            return new OperatorActionResult { Succeeded = true, Payment = payment };
        }

        // Cancel an order that was never authorized (or whose attempt failed): no money ever moved.
        if (payment == null)
        {
            payment = Payment.CreateFirstAttempt(orderId, order.BuyerId, order.Total(), _gateway.Currency);
        }

        payment.MarkCancelled();
        if (payment.Id == 0)
        {
            await _paymentRepository.AddAsync(payment);
        }
        else
        {
            await _paymentRepository.UpdateAsync(payment);
        }

        return new OperatorActionResult { Succeeded = true, Payment = payment };
    }

    public async Task<RefundAction> RefundOrderAsync(int orderId, decimal? amount, string idempotencyKey,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(idempotencyKey) || idempotencyKey.Length > 200)
        {
            return new RefundAction { Error = new PaymentError(PaymentErrorType.Validation,
                "A non-empty idempotencyKey of at most 200 characters is required.") };
        }

        var payment = await _paymentRepository.FirstOrDefaultAsync(new PaymentByOrderIdSpecification(orderId));
        if (payment == null || payment.PayPalCaptureId == null)
        {
            return new RefundAction { Error = new PaymentError(PaymentErrorType.Conflict,
                $"Order {orderId} has no captured payment to refund.") };
        }

        if (payment.Status is not (PaymentStatus.Captured or PaymentStatus.PartiallyRefunded))
        {
            return new RefundAction { Error = new PaymentError(PaymentErrorType.Conflict,
                $"Order {orderId} cannot be refunded while its payment is {payment.Status}.") };
        }

        // Idempotency: the same key always maps to the same refund, never a second charge-back.
        var existing = payment.Refunds.FirstOrDefault(r => r.IdempotencyKey == idempotencyKey);
        if (existing != null)
        {
            return new RefundAction { Succeeded = true, Refund = existing, Payment = payment };
        }

        var captured = payment.CapturedAmount ?? payment.Amount;
        var alreadyRefunded = payment.RefundedAmountCommitted;
        var remaining = captured - alreadyRefunded;

        var requested = amount ?? remaining;
        if (requested <= 0)
        {
            return new RefundAction { Error = new PaymentError(PaymentErrorType.Validation,
                "The refund amount must be positive.") };
        }

        if (requested > remaining)
        {
            return new RefundAction { Error = new PaymentError(PaymentErrorType.Conflict,
                $"Refund of {requested:0.00} {payment.Currency} exceeds the {remaining:0.00} {payment.Currency} still refundable on the capture.") };
        }

        // The caller key is scoped to this payment at the provider too, so the same caller
        // key on two different orders can never collide at PayPal.
        var refunded = await _gateway.RefundAsync($"{payment.PaymentKey}-{idempotencyKey}", payment.PayPalCaptureId,
            amount.HasValue ? requested : null, payment.Currency, ct);
        if (!refunded.Succeeded || refunded.Value == null)
        {
            return new RefundAction { Error = refunded.Error ?? new PaymentError(PaymentErrorType.ProviderError, "The refund failed.") };
        }

        var refund = new PaymentRefund(payment.Id, idempotencyKey, refunded.Value.Amount, refunded.Value.Currency,
            refunded.Value.RefundId, refunded.Value.Status);
        payment.AddRefund(refund);
        await _paymentRepository.UpdateAsync(payment);

        return new RefundAction { Succeeded = true, Refund = refund, Payment = payment };
    }

    public async Task<IReadOnlyList<MyOrderView>> GetMyOrdersAsync(string buyerId, CancellationToken ct = default)
    {
        var orders = await _orderRepository.ListAsync(new CustomerOrdersWithItemsSpecification(buyerId));
        var payments = await _paymentRepository.ListAsync(new PaymentsForBuyerSpecification(buyerId));
        var paymentByOrderId = payments.ToDictionary(p => p.OrderId);

        var views = orders
            .OrderByDescending(o => o.OrderDate)
            .Select(order =>
            {
                paymentByOrderId.TryGetValue(order.Id, out var payment);
                return new MyOrderView
                {
                    OrderId = order.Id,
                    OrderDate = order.OrderDate,
                    Total = order.Total(),
                    Currency = payment?.Currency ?? _gateway.Currency,
                    Status = OrderStatusFor(order, payment),
                    Items = order.OrderItems.Select(oi => new MyOrderItemView
                    {
                        CatalogItemId = oi.ItemOrdered.CatalogItemId,
                        Name = oi.ItemOrdered.ProductName,
                        PictureUri = oi.ItemOrdered.PictureUri,
                        UnitPrice = oi.UnitPrice,
                        Quantity = oi.Units
                    }).ToList(),
                    Payment = payment == null ? null : ToPaymentView(payment)
                };
            })
            .ToList();

        return views;
    }

    private static string OrderStatusFor(Order order, Payment? payment)
    {
        if (payment == null) return "AwaitingPayment";
        return payment.Status switch
        {
            PaymentStatus.Authorized => "Authorized",
            PaymentStatus.Captured => "Fulfilled",
            PaymentStatus.Refunded => "Refunded",
            PaymentStatus.PartiallyRefunded => "PartiallyRefunded",
            PaymentStatus.Voided => "Cancelled",
            PaymentStatus.Cancelled => "Cancelled",
            PaymentStatus.Failed => "AwaitingPayment",
            _ => "AwaitingPayment"
        };
    }

    public static PaymentView ToPaymentView(Payment payment)
    {
        return new PaymentView
        {
            PaymentId = payment.Id,
            State = payment.Status.ToString(),
            Amount = payment.Amount,
            Currency = payment.Currency,
            PayPalOrderId = payment.PayPalOrderId,
            AuthorizationId = payment.PayPalAuthorizationId,
            AuthorizationStatus = payment.PayPalAuthorizationStatus,
            AuthorizationExpiresAt = payment.AuthorizationExpiresAt,
            CaptureId = payment.PayPalCaptureId,
            CaptureStatus = payment.PayPalCaptureStatus,
            CapturedAmount = payment.CapturedAmount,
            PayPalFee = payment.PayPalFee,
            NetAmount = payment.NetAmount,
            CapturedAt = payment.CapturedAt,
            RefundedAmount = payment.RefundedAmountCommitted,
            Refunds = payment.Refunds.Select(r => new PaymentRefundView
            {
                RefundId = r.Id,
                PayPalRefundId = r.PayPalRefundId,
                IdempotencyKey = r.IdempotencyKey,
                Status = r.Status,
                Amount = r.Amount,
                Currency = r.Currency,
                CreatedAt = r.CreatedAt
            }).ToList()
        };
    }

    public async Task<CardActionResult> SaveCardAsync(string buyerId, CardInput card, CancellationToken ct = default)
    {
        if (card == null)
        {
            return new CardActionResult { Error = new PaymentError(PaymentErrorType.Validation, "Card details are required.") };
        }

        var vaulted = await _gateway.VaultCardAsync(buyerId, card, ct);
        if (!vaulted.Succeeded || vaulted.Value == null)
        {
            return new CardActionResult { Error = vaulted.Error ?? new PaymentError(PaymentErrorType.ProviderError, "The card could not be saved.") };
        }

        var saved = new SavedCard(buyerId, vaulted.Value.TokenId, vaulted.Value.CustomerId,
            vaulted.Value.Brand, vaulted.Value.LastDigits, vaulted.Value.Expiry, vaulted.Value.CardholderName);
        saved = await _savedCardRepository.AddAsync(saved);

        return new CardActionResult { Succeeded = true, Card = saved };
    }

    public async Task<IReadOnlyList<SavedCard>> GetSavedCardsAsync(string buyerId, CancellationToken ct = default)
    {
        return await _savedCardRepository.ListAsync(new SavedCardsForBuyerSpecification(buyerId));
    }

    public async Task<CardActionResult> DeleteSavedCardAsync(string buyerId, int paymentMethodId,
        CancellationToken ct = default)
    {
        var card = await _savedCardRepository.GetByIdAsync(paymentMethodId);
        if (card == null || card.BuyerId != buyerId)
        {
            return new CardActionResult { Error = new PaymentError(PaymentErrorType.NotFound,
                $"Payment method {paymentMethodId} was not found.") };
        }

        var deleted = await _gateway.DeleteVaultTokenAsync(card.VaultTokenId, ct);
        if (!deleted.Succeeded && deleted.Error?.Type is not (PaymentErrorType.NotFound or PaymentErrorType.Forbidden))
        {
            return new CardActionResult { Error = deleted.Error ?? new PaymentError(PaymentErrorType.ProviderError, "The payment method could not be removed.") };
        }

        if (!deleted.Succeeded && deleted.Error?.Type == PaymentErrorType.Forbidden)
        {
            // The sandbox/production account may not carry the vault-token-deletion grant.
            // The mandate still holds: the card disappears from the app and can never be
            // charged through it again. The orphaned provider-side token is logged for ops.
            _logger.LogWarning(
                "Vault token {VaultTokenId} for buyer {BuyerId} could not be revoked at the provider (forbidden); " +
                "the saved card was removed locally and can no longer be used to pay.", card.VaultTokenId, buyerId);}

        await _savedCardRepository.DeleteAsync(card);
        return new CardActionResult { Succeeded = true };
    }

    public async Task<GatewayResult<ReconciliationReport>> ReconcileAsync(DateTimeOffset from, DateTimeOffset to,
        CancellationToken ct = default)
    {
        var search = await _gateway.SearchTransactionsAsync(from, to, ct);
        if (!search.Succeeded || search.Value == null)
        {
            return GatewayResult<ReconciliationReport>.Failure(search.Error ??
                new PaymentError(PaymentErrorType.ProviderError, "The provider's transactions could not be listed."));
        }

        var payPalTransactions = search.Value.Transactions;
        var payPalIds = payPalTransactions
            .Select(t => t.TransactionId)
            .Where(id => !string.IsNullOrEmpty(id))
            .ToHashSet();

        var payments = await _paymentRepository.ListAsync(new PaymentsWithEventsBetweenSpecification(from, to));

        var shopPayments = new List<ShopPaymentRecord>();
        foreach (var payment in payments)
        {
            var invoiceId = $"eshop-{payment.PaymentKey}";
            if (payment.CapturedAt.HasValue && payment.CapturedAt >= from && payment.CapturedAt <= to &&
                payment.PayPalCaptureId != null)
            {
                shopPayments.Add(new ShopPaymentRecord
                {
                    OrderId = payment.OrderId,
                    PaymentKey = payment.PaymentKey,
                    Kind = "capture",
                    PayPalId = payment.PayPalCaptureId,
                    Amount = payment.CapturedAmount ?? payment.Amount,
                    Currency = payment.Currency,
                    Timestamp = payment.CapturedAt.Value
                });
            }

            foreach (var refund in payment.Refunds.Where(r => r.CreatedAt >= from && r.CreatedAt <= to &&
                         r.PayPalRefundId != null))
            {
                shopPayments.Add(new ShopPaymentRecord
                {
                    OrderId = payment.OrderId,
                    PaymentKey = payment.PaymentKey,
                    Kind = "refund",
                    PayPalId = refund.PayPalRefundId!,
                    Amount = refund.Amount,
                    Currency = refund.Currency,
                    Timestamp = refund.CreatedAt
                });
            }
        }

        foreach (var shopRecord in shopPayments)
        {
            // PayPal's reports echo the invoice id uppercased and re-formatted; match on the
            // unique payment key rather than an exact invoice string.
            shopRecord.Matched = payPalIds.Contains(shopRecord.PayPalId) ||
                                 payPalTransactions.Any(t =>
                                     t.InvoiceId != null &&
                                     t.InvoiceId.EndsWith(shopRecord.PaymentKey, StringComparison.OrdinalIgnoreCase));
        }

        var matchedPayPalIds = shopPayments.Select(s => s.PayPalId).ToHashSet();
        var matchedPaymentKeys = shopPayments.Select(s => s.PaymentKey).ToHashSet();

        bool IsMatchedTransaction(ReconciliationTransaction t) =>
            !string.IsNullOrEmpty(t.TransactionId) &&
            (matchedPayPalIds.Contains(t.TransactionId) ||
             (t.InvoiceId != null && t.InvoiceId.Length >= 32 &&
              matchedPaymentKeys.Contains(t.InvoiceId[^32..])));

        var payPalOnly = payPalTransactions
            .Where(t => !IsMatchedTransaction(t))
            .ToList();

        return GatewayResult<ReconciliationReport>.Success(new ReconciliationReport
        {
            From = from,
            To = to,
            PayPalTransactions = payPalTransactions,
            ShopPayments = shopPayments,
            PayPalOnly = payPalOnly,
            ShopOnly = shopPayments.Where(s => !s.Matched).ToList(),
            MatchedCount = shopPayments.Count(s => s.Matched),
            LastRefreshedDatetime = search.Value.LastRefreshedDatetime
        });
    }
}
