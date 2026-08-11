using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Ardalis.GuardClauses;
using Microsoft.eShopWeb.ApplicationCore.Entities;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Interfaces.Payments;
using Microsoft.eShopWeb.ApplicationCore.Specifications;

namespace Microsoft.eShopWeb.ApplicationCore.Services;

/// <summary>
/// Orchestrates the money movement for an order: place → authorize (hold) → fulfil (capture)
/// → cancel (void) or refund. It keeps the eShop <see cref="Payment"/> in step with the state
/// PayPal owns, and enforces the invariants (ownership, idempotency, refund bounds).
/// </summary>
public class PaymentService : IPaymentService
{
    private readonly IRepository<Order> _orderRepository;
    private readonly IRepository<CatalogItem> _itemRepository;
    private readonly IRepository<Payment> _paymentRepository;
    private readonly IRepository<SavedPaymentMethod> _savedMethodRepository;
    private readonly IPayPalGateway _gateway;
    private readonly IUriComposer _uriComposer;
    private readonly PayPalOptions _options;
    private readonly IAppLogger<PaymentService> _logger;

    public PaymentService(
        IRepository<Order> orderRepository,
        IRepository<CatalogItem> itemRepository,
        IRepository<Payment> paymentRepository,
        IRepository<SavedPaymentMethod> savedMethodRepository,
        IPayPalGateway gateway,
        IUriComposer uriComposer,
        PayPalOptions options,
        IAppLogger<PaymentService> logger)
    {
        _orderRepository = orderRepository;
        _itemRepository = itemRepository;
        _paymentRepository = paymentRepository;
        _savedMethodRepository = savedMethodRepository;
        _gateway = gateway;
        _uriComposer = uriComposer;
        _options = options;
        _logger = logger;
    }

    public async Task<Order> PlaceOrderAsync(string buyerId, IReadOnlyCollection<OrderLine> lines,
        Address shipToAddress, CancellationToken cancellationToken = default)
    {
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));
        Guard.Against.Null(shipToAddress, nameof(shipToAddress));
        if (lines is null || lines.Count == 0)
        {
            throw new PaymentException("An order must contain at least one line item.");
        }
        if (lines.Any(l => l.Quantity <= 0))
        {
            throw new PaymentException("Every order line must have a quantity of at least one.");
        }

        var catalogItemIds = lines.Select(l => l.CatalogItemId).Distinct().ToArray();
        var catalogItems = await _itemRepository.ListAsync(new CatalogItemsSpecification(catalogItemIds), cancellationToken);

        var missing = catalogItemIds.Except(catalogItems.Select(c => c.Id)).ToArray();
        if (missing.Length > 0)
        {
            throw new PaymentException($"Catalog item(s) not found: {string.Join(", ", missing)}.");
        }

        var items = lines.Select(line =>
        {
            var catalogItem = catalogItems.First(c => c.Id == line.CatalogItemId);
            var itemOrdered = new CatalogItemOrdered(catalogItem.Id, catalogItem.Name,
                _uriComposer.ComposePicUri(catalogItem.PictureUri));
            return new OrderItem(itemOrdered, catalogItem.Price, line.Quantity);
        }).ToList();

        var order = new Order(buyerId, shipToAddress, items);
        await _orderRepository.AddAsync(order, cancellationToken);

        var payment = new Payment(order.Id, buyerId, order.Total(), _options.CurrencyCode);
        await _paymentRepository.AddAsync(payment, cancellationToken);

        _logger.LogInformation($"Order {order.Id} placed for {buyerId}, total {order.Total()} {_options.CurrencyCode}, awaiting payment.");
        return order;
    }

    public async Task<Payment> AuthorizeAsync(string buyerId, int orderId, PayInstruction instruction,
        CancellationToken cancellationToken = default)
    {
        var payment = await GetPaymentForBuyerAsync(buyerId, orderId, cancellationToken);

        // Idempotent in effect: a double-click never authorizes twice.
        if (payment.Status == PaymentStatus.Authorized)
        {
            _logger.LogInformation($"Order {orderId} is already authorized; returning existing hold.");
            return payment;
        }
        if (payment.Status is not (PaymentStatus.AwaitingPayment or PaymentStatus.Failed))
        {
            throw new PaymentStateException($"Order {orderId} cannot be paid because its payment is {payment.Status}.");
        }

        var (card, vaultId, savedMethodId) = await ResolveFundingSourceAsync(buyerId, instruction, cancellationToken);

        var request = new AuthorizeRequest(
            Amount: payment.Amount,
            CurrencyCode: payment.CurrencyCode,
            InvoiceId: PaymentReference.For(payment),
            CustomId: orderId.ToString(CultureInfo.InvariantCulture),
            Description: $"eShopOnWeb order {orderId}",
            Card: card,
            VaultId: vaultId);

        var result = await _gateway.AuthorizeAsync(request, IdempotencyKeys.Authorize(payment.IdempotencySeed), cancellationToken);

        payment.MarkAuthorized(result.PayPalOrderId, result.AuthorizationId, result.Status, result.ExpiresAt, savedMethodId);
        await _paymentRepository.UpdateAsync(payment, cancellationToken);

        // Optionally vault the one-off card for later reuse (the primary save path is /api/payment-methods).
        if (instruction.SaveCard && card is not null)
        {
            try
            {
                var vaulted = await _gateway.VaultCardAsync(card, IdempotencyKeys.VaultOnPay(payment.IdempotencySeed), cancellationToken);
                await SaveVaultedCardAsync(buyerId, vaulted, cancellationToken);
            }
            catch (Exception ex)
            {
                // Saving the card is a convenience; never fail an authorized payment because vaulting failed.
                _logger.LogWarning($"Order {orderId} was authorized but the card could not be saved: {ex.Message}");
            }
        }

        _logger.LogInformation($"Order {orderId} authorized. PayPal order {result.PayPalOrderId}, authorization {result.AuthorizationId}.");
        return payment;
    }

    public async Task<Payment> FulfilAsync(int orderId, CancellationToken cancellationToken = default)
    {
        var payment = await GetPaymentOrThrowAsync(orderId, cancellationToken);

        // Idempotent: fulfilling an already-captured order returns the existing capture.
        if (payment.Status is PaymentStatus.Captured or PaymentStatus.PartiallyRefunded or PaymentStatus.Refunded)
        {
            return payment;
        }
        if (payment.Status != PaymentStatus.Authorized || string.IsNullOrEmpty(payment.AuthorizationId))
        {
            throw new PaymentStateException($"Order {orderId} cannot be fulfilled because its payment is {payment.Status}.");
        }

        CaptureResult capture;
        try
        {
            capture = await _gateway.CaptureAsync(payment.AuthorizationId,
                IdempotencyKeys.Capture(payment.AuthorizationId), cancellationToken);
        }
        catch (PayPalApiException ex) when (IsStaleAuthorization(ex))
        {
            capture = await RenewAndCaptureAsync(payment, orderId, ex, cancellationToken);
        }

        payment.MarkCaptured(capture.CaptureId, capture.Status, capture.GrossAmount, capture.PayPalFee, capture.NetAmount);
        await _paymentRepository.UpdateAsync(payment, cancellationToken);

        _logger.LogInformation($"Order {orderId} fulfilled. Captured {capture.GrossAmount} {capture.CurrencyCode}, fee {capture.PayPalFee}, net {capture.NetAmount}.");
        return payment;
    }

    private async Task<CaptureResult> RenewAndCaptureAsync(Payment payment, int orderId,
        PayPalApiException captureFailure, CancellationToken cancellationToken)
    {
        _logger.LogWarning($"Order {orderId} authorization {payment.AuthorizationId} is stale ({captureFailure.PayPalName}); renewing before capture.");

        AuthorizationResult renewed;
        try
        {
            renewed = await _gateway.ReauthorizeAsync(payment.AuthorizationId!, payment.Amount, payment.CurrencyCode,
                IdempotencyKeys.Reauthorize(payment.AuthorizationId!), cancellationToken);
        }
        catch (PayPalApiException reauthEx)
        {
            throw new AuthorizationExpiredException(
                $"Order {orderId}'s payment hold has expired and can no longer be renewed " +
                $"(PayPal: {reauthEx.PayPalName ?? "reauthorization failed"}). Ask the shopper to pay for the order again before it can be fulfilled.",
                reauthEx);
        }

        payment.MarkReauthorized(renewed.AuthorizationId, renewed.Status, renewed.ExpiresAt);
        await _paymentRepository.UpdateAsync(payment, cancellationToken);

        // Capture the renewed authorization under a key derived from its own (new) id.
        return await _gateway.CaptureAsync(renewed.AuthorizationId,
            IdempotencyKeys.Capture(renewed.AuthorizationId), cancellationToken);
    }

    public async Task<Payment> CancelAsync(int orderId, CancellationToken cancellationToken = default)
    {
        var payment = await GetPaymentOrThrowAsync(orderId, cancellationToken);

        if (payment.Status == PaymentStatus.Voided)
        {
            return payment;
        }
        if (payment.Status is PaymentStatus.Captured or PaymentStatus.PartiallyRefunded or PaymentStatus.Refunded)
        {
            throw new PaymentStateException($"Order {orderId} has already been fulfilled; use a refund to return the money.");
        }
        if (payment.Status == PaymentStatus.AwaitingPayment)
        {
            // Nothing was ever held; simply mark it cancelled so no further payment can be taken.
            payment.MarkVoided();
            await _paymentRepository.UpdateAsync(payment, cancellationToken);
            return payment;
        }
        if (payment.Status != PaymentStatus.Authorized || string.IsNullOrEmpty(payment.AuthorizationId))
        {
            throw new PaymentStateException($"Order {orderId} cannot be cancelled because its payment is {payment.Status}.");
        }

        await _gateway.VoidAuthorizationAsync(payment.AuthorizationId, IdempotencyKeys.Void(payment.AuthorizationId), cancellationToken);

        payment.MarkVoided();
        await _paymentRepository.UpdateAsync(payment, cancellationToken);

        _logger.LogInformation($"Order {orderId} cancelled; authorization {payment.AuthorizationId} voided, funds released.");
        return payment;
    }

    public async Task<(Payment Payment, PaymentRefund Refund)> RefundAsync(string buyerId, int orderId,
        decimal? amount, string idempotencyKey, CancellationToken cancellationToken = default)
    {
        Guard.Against.NullOrEmpty(idempotencyKey, nameof(idempotencyKey));
        var payment = await GetPaymentForBuyerAsync(buyerId, orderId, cancellationToken);

        if (payment.Status is not (PaymentStatus.Captured or PaymentStatus.PartiallyRefunded))
        {
            throw new PaymentStateException($"Order {orderId} cannot be refunded because its payment is {payment.Status}.");
        }
        if (string.IsNullOrEmpty(payment.CaptureId))
        {
            throw new PaymentStateException($"Order {orderId} has no capture to refund.");
        }

        // Idempotent: the same key never refunds twice.
        var existing = payment.FindRefundByIdempotencyKey(idempotencyKey);
        if (existing is not null)
        {
            return (payment, existing);
        }

        var remaining = payment.RefundableRemaining;
        if (amount.HasValue)
        {
            Guard.Against.NegativeOrZero(amount.Value, nameof(amount));
        }
        var refundAmount = decimal.Round(amount ?? remaining, 2, MidpointRounding.AwayFromZero);

        if (refundAmount <= 0m)
        {
            throw new RefundNotAllowedException($"Order {orderId} has already been fully refunded.");
        }
        if (refundAmount > remaining + 0.005m)
        {
            throw new RefundNotAllowedException(
                $"Refund of {refundAmount} {payment.CurrencyCode} exceeds the {remaining} {payment.CurrencyCode} still refundable on order {orderId}.");
        }

        var result = await _gateway.RefundAsync(payment.CaptureId, refundAmount, payment.CurrencyCode,
            IdempotencyKeys.Refund(payment.CaptureId, idempotencyKey), cancellationToken);

        var refund = payment.AddRefund(result.RefundId, refundAmount, idempotencyKey, result.Status);
        await _paymentRepository.UpdateAsync(payment, cancellationToken);

        _logger.LogInformation($"Order {orderId} refunded {refundAmount} {payment.CurrencyCode} (refund {result.RefundId}).");
        return (payment, refund);
    }

    public async Task<Payment> GetPaymentForBuyerAsync(string buyerId, int orderId, CancellationToken cancellationToken = default)
    {
        var payment = await _paymentRepository.FirstOrDefaultAsync(new PaymentByOrderIdSpec(orderId), cancellationToken);
        // Not-found and not-yours are indistinguishable to the caller, so one shopper cannot probe another's orders.
        if (payment is null || !string.Equals(payment.BuyerId, buyerId, StringComparison.Ordinal))
        {
            throw new PaymentNotFoundException($"No order {orderId} was found for the current user.");
        }
        return payment;
    }

    public async Task<IReadOnlyList<OrderWithPayment>> GetMyOrdersAsync(string buyerId, CancellationToken cancellationToken = default)
    {
        var orders = await _orderRepository.ListAsync(new CustomerOrdersWithItemsSpecification(buyerId), cancellationToken);
        var payments = await _paymentRepository.ListAsync(new PaymentsByBuyerSpec(buyerId), cancellationToken);
        var paymentsByOrder = payments.ToDictionary(p => p.OrderId);

        return orders
            .OrderByDescending(o => o.OrderDate)
            .Select(o => new OrderWithPayment(o, paymentsByOrder.GetValueOrDefault(o.Id)))
            .ToList();
    }

    public async Task<ReconciliationReport> ReconcileAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken = default)
    {
        if (to < from)
        {
            throw new PaymentException("The reconciliation 'to' date must be on or after the 'from' date.");
        }

        var transactions = await _gateway.SearchTransactionsAsync(from, to, cancellationToken);
        var payments = await _paymentRepository.ListAsync(new PaymentsWithPayPalActivitySpec(), cancellationToken);
        var paymentsByOrder = payments.ToDictionary(p => p.OrderId);

        var entries = new List<ReconciliationEntry>();
        var matchedOrderIds = new HashSet<int>();

        foreach (var tx in transactions)
        {
            var orderId = PaymentReference.TryGetOrderId(tx.InvoiceId);
            Payment? eShopPayment = null;
            if (orderId.HasValue)
            {
                paymentsByOrder.TryGetValue(orderId.Value, out eShopPayment);
            }

            if (eShopPayment is not null)
            {
                matchedOrderIds.Add(eShopPayment.OrderId);
                entries.Add(new ReconciliationEntry(
                    PayPalTransactionId: tx.TransactionId,
                    InvoiceId: tx.InvoiceId,
                    OrderId: eShopPayment.OrderId,
                    PayPalAmount: tx.Amount,
                    EShopAmount: eShopPayment.CapturedAmount ?? eShopPayment.Amount,
                    CurrencyCode: tx.CurrencyCode,
                    PayPalStatus: tx.Status,
                    MatchStatus: "Matched",
                    TransactionDate: tx.Date));
            }
            else
            {
                // PayPal has a transaction eShop cannot line up to an order.
                entries.Add(new ReconciliationEntry(
                    PayPalTransactionId: tx.TransactionId,
                    InvoiceId: tx.InvoiceId,
                    OrderId: orderId,
                    PayPalAmount: tx.Amount,
                    EShopAmount: null,
                    CurrencyCode: tx.CurrencyCode,
                    PayPalStatus: tx.Status,
                    MatchStatus: "PayPalOnly",
                    TransactionDate: tx.Date));
            }
        }

        // eShop payments PayPal's report does not (yet) show — commonly reporting lag in sandbox.
        foreach (var payment in payments.Where(p => !matchedOrderIds.Contains(p.OrderId)))
        {
            if (payment.CreatedAt >= from && payment.CreatedAt <= to)
            {
                entries.Add(new ReconciliationEntry(
                    PayPalTransactionId: null,
                    InvoiceId: PaymentReference.For(payment),
                    OrderId: payment.OrderId,
                    PayPalAmount: null,
                    EShopAmount: payment.CapturedAmount ?? payment.Amount,
                    CurrencyCode: payment.CurrencyCode,
                    PayPalStatus: null,
                    MatchStatus: "EShopOnly",
                    TransactionDate: null));
            }
        }

        return new ReconciliationReport(
            From: from,
            To: to,
            PayPalTransactionCount: transactions.Count,
            MatchedCount: entries.Count(e => e.MatchStatus == "Matched"),
            PayPalOnlyCount: entries.Count(e => e.MatchStatus == "PayPalOnly"),
            EShopOnlyCount: entries.Count(e => e.MatchStatus == "EShopOnly"),
            Entries: entries);
    }

    // ---- helpers ----

    private async Task<Payment> GetPaymentOrThrowAsync(int orderId, CancellationToken cancellationToken)
    {
        var payment = await _paymentRepository.FirstOrDefaultAsync(new PaymentByOrderIdSpec(orderId), cancellationToken);
        if (payment is null)
        {
            throw new PaymentNotFoundException($"No payment was found for order {orderId}.");
        }
        return payment;
    }

    private async Task<(CardDetails? Card, string? VaultId, int? SavedMethodId)> ResolveFundingSourceAsync(
        string buyerId, PayInstruction instruction, CancellationToken cancellationToken)
    {
        var hasCard = instruction.Card is not null;
        var hasSaved = instruction.SavedPaymentMethodId.HasValue;

        if (hasCard == hasSaved)
        {
            throw new PaymentException("Provide either card details or a saved card id to pay with — exactly one.");
        }

        if (hasCard)
        {
            return (instruction.Card, null, null);
        }

        var savedMethod = await _savedMethodRepository.FirstOrDefaultAsync(
            new SavedPaymentMethodByIdSpec(instruction.SavedPaymentMethodId!.Value), cancellationToken);
        if (savedMethod is null || !string.Equals(savedMethod.BuyerId, buyerId, StringComparison.Ordinal))
        {
            throw new PaymentNotFoundException($"No saved card {instruction.SavedPaymentMethodId} was found for the current user.");
        }

        return (null, savedMethod.PayPalVaultId, savedMethod.Id);
    }

    private async Task SaveVaultedCardAsync(string buyerId, VaultCardResult vaulted, CancellationToken cancellationToken)
    {
        var method = new SavedPaymentMethod(buyerId, vaulted.VaultId, vaulted.CardBrand,
            vaulted.LastDigits, vaulted.ExpiryYearMonth, vaulted.CardholderName);
        await _savedMethodRepository.AddAsync(method, cancellationToken);
    }

    /// <summary>
    /// True when a capture failure indicates the authorization has gone stale and should be
    /// renewed (reauthorized) rather than treated as a hard failure.
    /// </summary>
    private static bool IsStaleAuthorization(PayPalApiException ex)
    {
        if (ex.StatusCode is not (422 or 404))
        {
            return false;
        }

        var name = ex.PayPalName?.ToUpperInvariant() ?? string.Empty;
        var message = ex.Message.ToUpperInvariant();
        return name.Contains("EXPIRED")
            || name.Contains("AUTHORIZATION")
            || name is "RESOURCE_NOT_FOUND" or "INVALID_RESOURCE_ID"
            || message.Contains("AUTHORIZATION_EXPIRED")
            || message.Contains("AUTH_CAPTURE")
            || message.Contains("EXPIRED");
    }

    // Keys are built from globally-unique ids (a per-payment GUID seed, or PayPal's own resource ids)
    // so they stay stable across retries of the same operation yet never collide across payments,
    // in-memory restarts, or a reused sandbox account.
    private static class IdempotencyKeys
    {
        public static string Authorize(Guid seed) => $"eshop-auth-{seed:N}";
        public static string VaultOnPay(Guid seed) => $"eshop-vault-onpay-{seed:N}";
        public static string Capture(string authorizationId) => $"eshop-capture-{authorizationId}";
        public static string Reauthorize(string authorizationId) => $"eshop-reauth-{authorizationId}";
        public static string Void(string authorizationId) => $"eshop-void-{authorizationId}";
        public static string Refund(string captureId, string callerKey) => $"eshop-refund-{captureId}-{callerKey}";
    }
}
