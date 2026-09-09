using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
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
using NotFoundException = Microsoft.eShopWeb.ApplicationCore.Exceptions.NotFoundException;

namespace Microsoft.eShopWeb.ApplicationCore.Services;

/// <summary>
/// Orchestrates the PayPal-backed payment flows over the existing order model. Talks to PayPal
/// only through <see cref="IPayPalPaymentGateway"/> and keeps the state PayPal owns on the
/// <see cref="OrderPayment"/> aggregate so later requests can act on it.
/// </summary>
public class PaymentService : IPaymentService
{
    // Renew a hold that is within this window of expiring, before trying to capture.
    private static readonly TimeSpan ReauthorizeMargin = TimeSpan.FromHours(1);

    private readonly IRepository<Order> _orderRepository;
    private readonly IReadRepository<CatalogItem> _catalogRepository;
    private readonly IRepository<OrderPayment> _paymentRepository;
    private readonly IRepository<SavedPaymentMethod> _savedCardRepository;
    private readonly IUriComposer _uriComposer;
    private readonly IPayPalPaymentGateway _gateway;
    private readonly IPaymentSettings _settings;
    private readonly IAppLogger<PaymentService> _logger;

    public PaymentService(
        IRepository<Order> orderRepository,
        IReadRepository<CatalogItem> catalogRepository,
        IRepository<OrderPayment> paymentRepository,
        IRepository<SavedPaymentMethod> savedCardRepository,
        IUriComposer uriComposer,
        IPayPalPaymentGateway gateway,
        IPaymentSettings settings,
        IAppLogger<PaymentService> logger)
    {
        _orderRepository = orderRepository;
        _catalogRepository = catalogRepository;
        _paymentRepository = paymentRepository;
        _savedCardRepository = savedCardRepository;
        _uriComposer = uriComposer;
        _gateway = gateway;
        _settings = settings;
        _logger = logger;
    }

    public async Task<PlaceOrderResult> PlaceOrderAsync(string buyerId, IReadOnlyList<OrderLineInput> lines, ShippingAddressInput? address, CancellationToken cancellationToken = default)
    {
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));
        if (lines is null || lines.Count == 0)
        {
            throw new PaymentValidationException("An order must contain at least one line item.");
        }

        foreach (var line in lines)
        {
            if (line.Quantity <= 0)
            {
                throw new PaymentValidationException($"Quantity for catalog item {line.CatalogItemId} must be greater than zero.");
            }
        }

        var ids = lines.Select(l => l.CatalogItemId).Distinct().ToArray();
        var catalogItems = await _catalogRepository.ListAsync(new CatalogItemsSpecification(ids), cancellationToken);
        var catalogById = catalogItems.ToDictionary(c => c.Id);

        var missing = ids.Where(id => !catalogById.ContainsKey(id)).ToList();
        if (missing.Count > 0)
        {
            throw new PaymentValidationException($"Unknown catalog item(s): {string.Join(", ", missing)}.");
        }

        var items = new List<OrderItem>();
        foreach (var line in lines)
        {
            var catalogItem = catalogById[line.CatalogItemId];
            var itemOrdered = new CatalogItemOrdered(catalogItem.Id, catalogItem.Name, _uriComposer.ComposePicUri(catalogItem.PictureUri));
            items.Add(new OrderItem(itemOrdered, catalogItem.Price, line.Quantity));
        }

        var shipTo = address is not null
            ? new Address(address.Street, address.City, address.State, address.Country, address.ZipCode)
            : new Address("N/A", "N/A", "N/A", "N/A", "00000");

        var order = new Order(buyerId, shipTo, items);
        order = await _orderRepository.AddAsync(order, cancellationToken);

        var currency = _settings.Currency;
        var payment = new OrderPayment(order.Id, buyerId, order.Total(), currency);
        await _paymentRepository.AddAsync(payment, cancellationToken);

        _logger.LogInformation("Placed order {0} for buyer with total {1} {2} awaiting payment.", order.Id, order.Total(), currency);
        return new PlaceOrderResult(order.Id, order.Total(), currency, payment.Status.ToString());
    }

    public async Task<OrderPaymentView> AuthorizeOrderAsync(string buyerId, int orderId, PayInstruction instruction, CancellationToken cancellationToken = default)
    {
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));
        var payment = await LoadOwnedPaymentAsync(buyerId, orderId, cancellationToken);
        var order = await LoadOrderAsync(orderId, cancellationToken);

        // Idempotent in effect: a double-click never authorizes twice.
        if (payment.IsAuthorized)
        {
            if (payment.Status == OrderPaymentStatus.Authorized)
            {
                return ToView(payment, order);
            }
            throw new PaymentStateException($"Order {orderId} has already progressed beyond authorization (status: {payment.Status}).");
        }

        if (payment.Status != OrderPaymentStatus.AwaitingPayment)
        {
            throw new PaymentStateException($"Order {orderId} cannot be paid in its current state ({payment.Status}).");
        }

        SavedPaymentMethod? savedCard = null;
        string? vaultId = null;
        PayPalCardDetails? card = instruction.Card;

        if (instruction.SavedPaymentMethodId is int savedId)
        {
            savedCard = await _savedCardRepository.FirstOrDefaultAsync(new SavedPaymentMethodByIdSpec(savedId, buyerId), cancellationToken)
                ?? throw new NotFoundException($"Saved card {savedId} was not found.");
            vaultId = savedCard.PayPalVaultId;
            card = null;
        }
        else if (card is null)
        {
            throw new PaymentValidationException("Provide either card details or a saved paymentMethodId to pay.");
        }

        var request = new AuthorizeOrderRequest(
            ReferenceId: orderId.ToString(CultureInfo.InvariantCulture),
            InvoiceId: payment.InvoiceId,
            // Unique per payment so reconciliation never false-matches a prior run's transaction.
            CustomId: payment.InvoiceId,
            Amount: payment.Amount,
            CurrencyCode: payment.CurrencyCode,
            IdempotencyKey: $"auth-{payment.IdempotencyToken}",
            Card: card,
            VaultId: vaultId);

        var auth = await _gateway.AuthorizeOrderAsync(request, cancellationToken);

        var description = savedCard is not null
            ? $"{savedCard.Brand} ****{savedCard.LastFourDigits}"
            : (auth.CardBrand is not null ? $"{auth.CardBrand} ****{auth.CardLastDigits}" : null);

        payment.MarkAuthorized(auth.PayPalOrderId, auth.AuthorizationId, auth.Status, auth.ExpiresAt, description, savedCard?.Id);
        await _paymentRepository.UpdateAsync(payment, cancellationToken);
        _logger.LogInformation("Authorized order {0}: PayPal order {1}, authorization {2} ({3}).", orderId, auth.PayPalOrderId, auth.AuthorizationId, auth.Status);

        // Optional: also vault the one-off card for reuse, without letting a vault failure fail the payment.
        if (instruction.SaveCard && card is not null)
        {
            try
            {
                await SaveCardAsync(buyerId, card, cancellationToken);
            }
            catch (Exception ex)
            {
                _logger.LogWarning("Order {0} authorized but saving the card for reuse failed: {1}", orderId, ex.Message);
            }
        }

        return ToView(payment, order);
    }

    public async Task<OrderPaymentView> FulfilOrderAsync(int orderId, CancellationToken cancellationToken = default)
    {
        var payment = await LoadPaymentAsync(orderId, cancellationToken);
        var order = await LoadOrderAsync(orderId, cancellationToken);

        // Idempotent in effect: capturing an already-fulfilled order never takes money twice.
        if (payment.Status == OrderPaymentStatus.Fulfilled || payment.IsCaptured)
        {
            return ToView(payment, order);
        }

        if (payment.Status != OrderPaymentStatus.Authorized || payment.AuthorizationId is null)
        {
            throw new PaymentStateException($"Order {orderId} is not awaiting fulfilment (status: {payment.Status}).");
        }

        // Renew a hold that has gone (or is about to go) stale, rather than failing the fulfilment.
        await EnsureUsableAuthorizationAsync(payment, cancellationToken);

        GatewayCapture capture;
        try
        {
            capture = await _gateway.CaptureAuthorizationAsync(payment.AuthorizationId!, payment.Amount, payment.CurrencyCode, $"cap-{payment.IdempotencyToken}", cancellationToken);
        }
        catch (PayPalGatewayException ex) when (IsExpiredAuthorization(ex))
        {
            // The hold expired between our check and the capture; try to renew once, then capture again.
            _logger.LogWarning("Capture of order {0} hit an expired authorization; attempting to renew.", orderId);
            await ReauthorizeOrThrowAsync(payment, orderId, cancellationToken);
            capture = await _gateway.CaptureAuthorizationAsync(payment.AuthorizationId!, payment.Amount, payment.CurrencyCode, $"cap-{payment.IdempotencyToken}-renewed", cancellationToken);
        }

        payment.MarkFulfilled(capture.CaptureId, capture.Status, capture.GrossAmount, capture.PayPalFee, capture.NetAmount);
        await _paymentRepository.UpdateAsync(payment, cancellationToken);
        _logger.LogInformation("Fulfilled order {0}: captured {1} {2}, fee {3}, net {4}.", orderId, capture.GrossAmount, capture.CurrencyCode, capture.PayPalFee, capture.NetAmount);

        return ToView(payment, order);
    }

    public async Task<OrderPaymentView> CancelOrderAsync(int orderId, CancellationToken cancellationToken = default)
    {
        var payment = await LoadPaymentAsync(orderId, cancellationToken);
        var order = await LoadOrderAsync(orderId, cancellationToken);

        if (payment.Status == OrderPaymentStatus.Cancelled)
        {
            return ToView(payment, order);
        }

        if (payment.IsCaptured || payment.Status is OrderPaymentStatus.Fulfilled or OrderPaymentStatus.Refunded or OrderPaymentStatus.PartiallyRefunded)
        {
            throw new PaymentStateException($"Order {orderId} has already been fulfilled; use a refund instead of cancel.");
        }

        if (payment.IsAuthorized && payment.AuthorizationId is not null)
        {
            await _gateway.VoidAuthorizationAsync(payment.AuthorizationId, $"void-{payment.IdempotencyToken}", cancellationToken);
            _logger.LogInformation("Released hold on order {0} (voided authorization {1}).", orderId, payment.AuthorizationId);
        }

        payment.MarkCancelled();
        await _paymentRepository.UpdateAsync(payment, cancellationToken);
        return ToView(payment, order);
    }

    public async Task<RefundResultView> RefundOrderAsync(string buyerId, int orderId, decimal? amount, string idempotencyKey, CancellationToken cancellationToken = default)
    {
        Guard.Against.NullOrEmpty(idempotencyKey, nameof(idempotencyKey));
        var payment = await LoadOwnedPaymentAsync(buyerId, orderId, cancellationToken);

        if (!payment.IsCaptured || payment.CaptureId is null)
        {
            throw new PaymentStateException($"Order {orderId} has not been fulfilled; there is nothing captured to refund.");
        }

        // Idempotent replay: same key returns the same refund, never a second one.
        var existing = payment.FindRefundByIdempotencyKey(idempotencyKey);
        if (existing is not null)
        {
            return new RefundResultView(existing.Id, existing.PayPalRefundId, existing.Status, existing.Amount, existing.CurrencyCode, payment.RefundableAmount());
        }

        var refundable = payment.RefundableAmount();
        if (refundable <= 0m)
        {
            throw new PaymentValidationException($"Order {orderId} has already been fully refunded.");
        }

        var refundAmount = amount ?? refundable;
        if (refundAmount <= 0m)
        {
            throw new PaymentValidationException("Refund amount must be greater than zero.");
        }
        // A partly-refunded order must never become refundable beyond what was captured.
        if (refundAmount > refundable)
        {
            throw new PaymentValidationException($"Refund of {refundAmount} {payment.CurrencyCode} exceeds the refundable amount of {refundable} {payment.CurrencyCode}.");
        }

        // Namespace the PayPal request-id with the payment token so the caller's key stays idempotent
        // for this capture without colliding across runs or with other captures.
        var refund = await _gateway.RefundCaptureAsync(payment.CaptureId, refundAmount, payment.CurrencyCode, $"refund-{payment.IdempotencyToken}-{idempotencyKey}", cancellationToken);
        var refundEntity = new PaymentRefund(idempotencyKey, refund.RefundId, refund.Amount, refund.CurrencyCode, refund.Status);
        payment.AddRefund(refundEntity);
        await _paymentRepository.UpdateAsync(payment, cancellationToken);
        _logger.LogInformation("Refunded {0} {1} on order {2} (PayPal refund {3}).", refund.Amount, refund.CurrencyCode, orderId, refund.RefundId);

        return new RefundResultView(refundEntity.Id, refundEntity.PayPalRefundId, refundEntity.Status, refundEntity.Amount, refundEntity.CurrencyCode, payment.RefundableAmount());
    }

    public async Task<OrderPaymentView?> GetOrderAsync(string buyerId, int orderId, CancellationToken cancellationToken = default)
    {
        var payment = await _paymentRepository.FirstOrDefaultAsync(new OrderPaymentByOrderIdSpec(orderId), cancellationToken);
        if (payment is null || payment.BuyerId != buyerId)
        {
            return null;
        }
        var order = await LoadOrderAsync(orderId, cancellationToken);
        return ToView(payment, order);
    }

    public async Task<IReadOnlyList<OrderPaymentView>> GetMyOrdersAsync(string buyerId, CancellationToken cancellationToken = default)
    {
        var payments = await _paymentRepository.ListAsync(new OrderPaymentsByBuyerSpec(buyerId), cancellationToken);
        var orders = await _orderRepository.ListAsync(new CustomerOrdersSpecification(buyerId), cancellationToken);
        var ordersById = orders.ToDictionary(o => o.Id);

        return payments
            .Select(p => ToView(p, ordersById.GetValueOrDefault(p.OrderId)))
            .ToList();
    }

    public async Task<SavedCardResult> SaveCardAsync(string buyerId, PayPalCardDetails card, CancellationToken cancellationToken = default)
    {
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));
        Guard.Against.Null(card, nameof(card));
        if (string.IsNullOrWhiteSpace(card.Number) || string.IsNullOrWhiteSpace(card.Expiry) || string.IsNullOrWhiteSpace(card.SecurityCode))
        {
            throw new PaymentValidationException("Card number, expiry (YYYY-MM) and security code are required to save a card.");
        }

        // Group a shopper's vaulted cards under one stable PayPal customer, reused across saves.
        var existingCustomerId = (await _savedCardRepository.FirstOrDefaultAsync(new LatestSavedPaymentMethodByBuyerSpec(buyerId), cancellationToken))?.PayPalCustomerId;
        var merchantCustomerId = BuildMerchantCustomerId(buyerId);

        var vaulted = await _gateway.VaultCardAsync(new VaultCardRequest(card, merchantCustomerId, existingCustomerId), cancellationToken);

        var saved = new SavedPaymentMethod(
            buyerId,
            vaulted.VaultId,
            vaulted.CustomerId ?? existingCustomerId,
            vaulted.Brand,
            vaulted.LastDigits,
            vaulted.Expiry,
            card.CardholderName ?? string.Empty);
        saved = await _savedCardRepository.AddAsync(saved, cancellationToken);
        _logger.LogInformation("Saved a {0} card ending {1} for a shopper (vault token {2}).", saved.Brand, saved.LastFourDigits, saved.PayPalVaultId);

        return new SavedCardResult(saved.Id, ToView(saved));
    }

    public async Task<IReadOnlyList<SavedCardView>> GetSavedCardsAsync(string buyerId, CancellationToken cancellationToken = default)
    {
        var cards = await _savedCardRepository.ListAsync(new SavedPaymentMethodsByBuyerSpec(buyerId), cancellationToken);
        return cards.Select(ToView).ToList();
    }

    public async Task DeleteSavedCardAsync(string buyerId, int paymentMethodId, CancellationToken cancellationToken = default)
    {
        var card = await _savedCardRepository.FirstOrDefaultAsync(new SavedPaymentMethodByIdSpec(paymentMethodId, buyerId), cancellationToken)
            ?? throw new NotFoundException($"Saved card {paymentMethodId} was not found.");

        await _gateway.DeleteVaultedCardAsync(card.PayPalVaultId, cancellationToken);
        await _savedCardRepository.DeleteAsync(card, cancellationToken);
        _logger.LogInformation("Removed saved card {0} (vault token {1}).", paymentMethodId, card.PayPalVaultId);
    }

    public async Task<ReconciliationReport> ReconcileAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken = default)
    {
        if (to < from)
        {
            throw new PaymentValidationException("The reconciliation 'to' must not be earlier than 'from'.");
        }

        var transactions = await _gateway.SearchTransactionsAsync(from, to, cancellationToken);
        var payments = await _paymentRepository.ListAsync(new AllOrderPaymentsSpec(), cancellationToken);
        var paymentsByInvoice = payments
            .GroupBy(p => p.InvoiceId)
            .ToDictionary(g => g.Key, g => g.First());

        var matched = new List<ReconciliationEntry>();
        var inPayPalOnly = new List<ReconciliationEntry>();
        var seenInvoices = new HashSet<string>();

        foreach (var txn in transactions)
        {
            // Match on the payment's unique invoice id, carried in PayPal's invoice_id or custom_field.
            OrderPayment? match = null;
            if (!string.IsNullOrEmpty(txn.InvoiceId))
            {
                paymentsByInvoice.TryGetValue(txn.InvoiceId!, out match);
            }
            if (match is null && !string.IsNullOrEmpty(txn.CustomField))
            {
                paymentsByInvoice.TryGetValue(txn.CustomField!, out match);
            }

            if (match is not null)
            {
                seenInvoices.Add(match.InvoiceId);
            }

            var entry = new ReconciliationEntry(txn.TransactionId, txn.Status, txn.Amount, txn.CurrencyCode, txn.FeeAmount, txn.InitiationDate, txn.InvoiceId, match?.OrderId);
            (match is not null ? matched : inPayPalOnly).Add(entry);
        }

        // eShop payments that moved money but PayPal reporting has not surfaced yet (lag) or at all.
        var inEShopOnly = payments
            .Where(p => p.IsCaptured && !seenInvoices.Contains(p.InvoiceId))
            .Select(p => new UnreconciledOrder(p.OrderId, p.InvoiceId, p.CaptureId, p.CapturedAmount, p.CurrencyCode, p.Status.ToString()))
            .ToList();

        var note = "PayPal transaction reporting lags live activity; recently created payments may not appear yet. " +
                   "Entries under 'inEShopNotInPayPal' can therefore be a reporting delay rather than a true discrepancy.";

        return new ReconciliationReport(from, to, DateTimeOffset.UtcNow, transactions.Count, matched, inPayPalOnly, inEShopOnly, note);
    }

    // --- helpers ---

    private async Task EnsureUsableAuthorizationAsync(OrderPayment payment, CancellationToken cancellationToken)
    {
        var expiresAt = payment.AuthorizationExpiresAt;
        if (expiresAt is null)
        {
            // Unknown expiry: confirm the current state from PayPal.
            try
            {
                var info = await _gateway.GetAuthorizationAsync(payment.AuthorizationId!, cancellationToken);
                payment.RenewAuthorization(info.AuthorizationId, info.Status, info.ExpiresAt);
                expiresAt = info.ExpiresAt;
            }
            catch (PayPalGatewayException ex)
            {
                _logger.LogWarning("Could not read authorization {0} for order {1}: {2}", payment.AuthorizationId, payment.OrderId, ex.Message);
                return;
            }
        }

        if (expiresAt is not null && expiresAt.Value - ReauthorizeMargin <= DateTimeOffset.UtcNow)
        {
            _logger.LogInformation("Authorization for order {0} is stale (expires {1}); renewing before capture.", payment.OrderId, expiresAt);
            await ReauthorizeOrThrowAsync(payment, payment.OrderId, cancellationToken);
        }
    }

    private async Task ReauthorizeOrThrowAsync(OrderPayment payment, int orderId, CancellationToken cancellationToken)
    {
        try
        {
            var renewed = await _gateway.ReauthorizeAsync(payment.AuthorizationId!, payment.Amount, payment.CurrencyCode, $"reauth-{payment.IdempotencyToken}-{DateTimeOffset.UtcNow:yyyyMMddHHmmss}", cancellationToken);
            payment.RenewAuthorization(renewed.AuthorizationId, renewed.Status, renewed.ExpiresAt);
            await _paymentRepository.UpdateAsync(payment, cancellationToken);
            _logger.LogInformation("Renewed authorization for order {0}: {1} ({2}).", orderId, renewed.AuthorizationId, renewed.Status);
        }
        catch (PayPalGatewayException ex)
        {
            throw new PaymentValidationException(
                $"The authorization for order {orderId} has expired and can no longer be renewed " +
                $"(PayPal: {ex.Name}{(ex.Issues.Count > 0 ? " / " + string.Join(", ", ex.Issues) : string.Empty)}). " +
                "Ask the shopper to place and pay for a new order.");
        }
    }

    private static bool IsExpiredAuthorization(PayPalGatewayException ex) =>
        ex.HasIssue("AUTHORIZATION_EXPIRED", "AUTH_CAPTURE_CURRENCY_MISMATCH", "PAYMENT_STATE_INVALID", "AUTHORIZATION_VOIDED");

    private async Task<OrderPayment> LoadPaymentAsync(int orderId, CancellationToken cancellationToken)
    {
        return await _paymentRepository.FirstOrDefaultAsync(new OrderPaymentByOrderIdSpec(orderId), cancellationToken)
            ?? throw new NotFoundException($"Order {orderId} was not found.");
    }

    private async Task<OrderPayment> LoadOwnedPaymentAsync(string buyerId, int orderId, CancellationToken cancellationToken)
    {
        var payment = await LoadPaymentAsync(orderId, cancellationToken);
        if (payment.BuyerId != buyerId)
        {
            // Do not reveal that the order exists for another shopper.
            throw new NotFoundException($"Order {orderId} was not found.");
        }
        return payment;
    }

    private async Task<Order?> LoadOrderAsync(int orderId, CancellationToken cancellationToken)
    {
        return await _orderRepository.FirstOrDefaultAsync(new OrderWithItemsByIdSpec(orderId), cancellationToken);
    }

    private static string BuildMerchantCustomerId(string buyerId)
    {
        // Stable, non-reversible id ≤ 64 chars grouping a shopper's vaulted cards at PayPal.
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(buyerId));
        var hex = Convert.ToHexString(bytes).ToLowerInvariant();
        return "eshop-" + hex[..32];
    }

    private OrderPaymentView ToView(OrderPayment payment, Order? order)
    {
        var refunds = payment.Refunds
            .OrderBy(r => r.CreatedAt)
            .Select(r => new RefundView(r.Id, r.PayPalRefundId, r.Amount, r.CurrencyCode, r.Status, r.CreatedAt))
            .ToList();

        return new OrderPaymentView(
            payment.OrderId,
            order?.OrderDate ?? payment.CreatedAt,
            payment.Status.ToString(),
            payment.Amount,
            payment.CurrencyCode,
            payment.PayPalOrderId,
            payment.AuthorizationId,
            payment.AuthorizationStatus,
            payment.AuthorizationExpiresAt,
            payment.CaptureId,
            payment.CaptureStatus,
            payment.CapturedAmount,
            payment.PayPalFee,
            payment.NetAmount,
            payment.PaymentInstrumentDescription,
            payment.RefundableAmount(),
            refunds);
    }

    private static SavedCardView ToView(SavedPaymentMethod card) =>
        new(card.Id, card.Brand, card.LastFourDigits, card.Expiry, card.CardholderName, card.CreatedAt);
}
