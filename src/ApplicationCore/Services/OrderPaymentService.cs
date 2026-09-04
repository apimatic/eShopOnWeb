using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Ardalis.GuardClauses;
using Microsoft.eShopWeb.ApplicationCore.Common;
using Microsoft.eShopWeb.ApplicationCore.Entities;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentMethodAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.PaymentGateway;
using Microsoft.eShopWeb.ApplicationCore.Settings;
using Microsoft.eShopWeb.ApplicationCore.Specifications;

namespace Microsoft.eShopWeb.ApplicationCore.Services;

/// <summary>
/// Orchestrates order lifecycle against the payment provider: place (awaiting payment),
/// pay (authorize/hold), fulfil (capture; renewing stale holds), cancel (void/release),
/// refund (full or partial, caller-idempotent). Enforces shopper ownership, the state
/// machine and payment idempotency; the gateway owns wire contracts.
/// </summary>
public class OrderPaymentService : IOrderPaymentService
{
    private const string ProviderName = "paypal";

    private readonly IRepository<Order> _orderRepository;
    private readonly IRepository<CatalogItem> _catalogItemRepository;
    private readonly IRepository<SavedPaymentMethod> _paymentMethodRepository;
    private readonly IPaymentGateway _gateway;
    private readonly IUriComposer _uriComposer;
    private readonly IUnitOfWork _unitOfWork;
    private readonly PayPalSettings _settings;
    private readonly IAppLogger<OrderPaymentService> _logger;

    public OrderPaymentService(
        IRepository<Order> orderRepository,
        IRepository<CatalogItem> catalogItemRepository,
        IRepository<SavedPaymentMethod> paymentMethodRepository,
        IPaymentGateway gateway,
        IUriComposer uriComposer,
        IUnitOfWork unitOfWork,
        PayPalSettings settings,
        IAppLogger<OrderPaymentService> logger)
    {
        _orderRepository = orderRepository;
        _catalogItemRepository = catalogItemRepository;
        _paymentMethodRepository = paymentMethodRepository;
        _gateway = gateway;
        _uriComposer = uriComposer;
        _unitOfWork = unitOfWork;
        _settings = settings;
        _logger = logger;
    }

    public async Task<PlaceOrderResult> PlaceOrderAsync(string buyerId, IReadOnlyList<PlaceOrderLine> lines, Address? shipToAddress, CancellationToken ct = default)
    {
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));

        if (lines is null || lines.Count == 0)
        {
            throw new ValidationFailureException("At least one catalog item is required to place an order.");
        }
        if (lines.Any(l => l.Quantity <= 0))
        {
            throw new ValidationFailureException("Every line must have a quantity greater than zero.");
        }
        if (lines.GroupBy(l => l.CatalogItemId).Any(g => g.Count() > 1))
        {
            throw new ValidationFailureException("A catalog item may appear only once per order.");
        }

        var catalogItems = await _catalogItemRepository.ListAsync(new CatalogItemsSpecification(lines.Select(l => l.CatalogItemId).ToArray()), ct);
        var missing = lines.Where(l => catalogItems.All(c => c.Id != l.CatalogItemId)).Select(l => l.CatalogItemId).ToList();
        if (missing.Count > 0)
        {
            throw new ValidationFailureException($"Catalog item(s) not found: {string.Join(", ", missing)}.");
        }

        var orderItems = lines.Select(line =>
        {
            var catalogItem = catalogItems.First(c => c.Id == line.CatalogItemId);
            var itemOrdered = new CatalogItemOrdered(catalogItem.Id, catalogItem.Name, _uriComposer.ComposePicUri(catalogItem.PictureUri));
            return new OrderItem(itemOrdered, catalogItem.Price, line.Quantity);
        }).ToList();

        var order = new Order(buyerId, shipToAddress ?? new Address(string.Empty, string.Empty, string.Empty, string.Empty, string.Empty), orderItems);

        order = await _orderRepository.AddAsync(order, ct);

        _logger.LogInformation($"Order {order.Id} placed for buyer, status {order.Status}.");
        return new PlaceOrderResult(order);
    }

    public async Task<PayResult> PayAsync(int orderId, string buyerId, PayCommand command, CancellationToken ct = default)
    {
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));
        Guard.Against.Null(command, nameof(command));

        var hasCard = command.Card is not null;
        var hasSaved = !string.IsNullOrWhiteSpace(command.SavedPaymentMethodId);
        if (hasCard == hasSaved)
        {
            throw new ValidationFailureException("Provide exactly one payment source: card details or a saved paymentMethodId.");
        }

        using var _ = await AsyncKeyedLock.LockAsync($"order:{orderId}", ct);

        var order = await LoadOwnedOrderAsync(orderId, buyerId, ct);

        switch (order.Status)
        {
            case OrderStatus.Cancelled:
                throw new OrderStateException("This order was cancelled; it can no longer be paid.");
            case OrderStatus.Authorized:
                return new PayResult(order, order.Payment!, Replayed: true);
            case OrderStatus.Fulfilled:
                return new PayResult(order, order.Payment!, Replayed: true);
        }

        if (command.ExpectedAmount.HasValue && RoundMoney(command.ExpectedAmount.Value) != RoundMoney(order.Total()))
        {
            throw new ValidationFailureException($"Expected amount {command.ExpectedAmount.Value.ToString("0.00", CultureInfo.InvariantCulture)} does not match the order total {order.Total().ToString("0.00", CultureInfo.InvariantCulture)}.");
        }

        var source = new GatewayAuthorizeSource(command.Card, null, null);
        if (hasSaved)
        {
            source = await BuildVaultSourceAsync(buyerId, command.SavedPaymentMethodId!, ct);
        }

        var amount = RoundMoney(order.Total());
        var currency = ResolveCurrency();

        var pending = order.Payment is { } p && p.HasPendingAuthorizationToRecover ? p : null;
        // The merchant account enforces unique invoice ids per transaction, so each hold
        // attempt carries a nonce; it is persisted so replays act on the same hold, never
        // a second one. custom_id stays the deterministic "eshop-order-{id}" correlation key.
        var invoiceReference = string.IsNullOrEmpty(pending?.InvoiceReference)
            ? NewInvoiceReference(order.Id)
            : pending!.InvoiceReference;
        GatewayAuthorization authorization;
        try
        {
            authorization = pending is not null
                ? await RecoverAuthorizationAsync(pending, source, currency, ct)
                : await _gateway.AuthorizeAsync(new GatewayAuthorizeRequest(amount, currency, invoiceReference, OrderInvoiceReference(order.Id), source), ct);
        }
        catch (PaymentGatewayException ex) when (ex.Kind == PaymentFailureKind.OutcomeUnknown && ex.ProviderOrderId is not null)
        {
            // Hold may exist on PayPal's side under a known provider order: remember it so a
            // replayed pay recovers that hold instead of taking the shopper's money twice.
            var pendingPayment = PaymentDetails.ForPendingProviderOrder(ProviderName, ex.ProviderOrderId, currency, amount);
            pendingPayment.NoteInvoiceReference(invoiceReference);
            order.RecordPendingProviderOrder(pendingPayment);
            await _unitOfWork.SaveChangesAsync(ct);
            throw;
        }

        var payment = new PaymentDetails(
            ProviderName,
            authorization.ProviderOrderId ?? pending?.ProviderOrderId ?? string.Empty,
            authorization.AuthorizationId,
            authorization.Status,
            authorization.Amount,
            authorization.Currency,
            authorization.ExpirationTime,
            authorization.CreatedTime,
            authorization.NetworkTransactionReference);
        payment.NoteVaultTokenId(source.VaultTokenId);
        payment.NoteInvoiceReference(invoiceReference);

        order.RecordAuthorization(payment);
        await _unitOfWork.SaveChangesAsync(ct);

        _logger.LogInformation($"Order {order.Id} authorized for {authorization.Amount:0.00} {authorization.Currency} (auth {authorization.AuthorizationId}).");
        return new PayResult(order, payment, Replayed: false);
    }

    public async Task<FulfilResult> FulfilAsync(int orderId, CancellationToken ct = default)
    {
        using var _ = await AsyncKeyedLock.LockAsync($"order:{orderId}", ct);

        var order = await LoadOrderAsync(orderId, ct);

        if (order.Status == OrderStatus.Cancelled)
        {
            throw new OrderStateException("This order was cancelled; it cannot be fulfilled.");
        }
        if (order.Status == OrderStatus.Fulfilled && order.Payment is { IsCaptured: true })
        {
            return new FulfilResult(order, order.Payment, Replayed: true);
        }
        if (order.Payment is null || string.IsNullOrEmpty(order.Payment.ProviderOrderId))
        {
            throw new OrderStateException("This order has not been paid yet; it cannot be fulfilled.");
        }

        var payment = order.Payment;

        if (payment.HasPendingAuthorizationToRecover)
        {
            payment = await SettlePendingAuthorizationAsync(order, payment, ct);
        }

        if (payment.AuthorizationExpired)
        {
            payment = await RenewExpiredAuthorizationAsync(order, payment, ct);
        }

        GatewayCapture capture;
        try
        {
            capture = await _gateway.CaptureAsync(payment.AuthorizationId, payment.AuthorizedAmount, payment.CurrencyCode, ct);
        }
        catch (PaymentGatewayException ex) when (ex.Kind is PaymentFailureKind.Conflict or PaymentFailureKind.ResourceNotFound or PaymentFailureKind.ProviderRejected)
        {
            // The capture may already exist on PayPal's side (replayed attempt, a previously
            // unknown outcome, or a "minimal" first response that never got recorded):
            // settle from provider state rather than failing the fulfilment.
            var settled = await SettleCaptureFromProviderOrderAsync(order, ex, ct);
            if (settled is null)
            {
                throw;
            }
            capture = settled;
        }

        if (capture.FeeAmount is null && capture.NetAmount is null && capture.Status == CaptureStatuses.Pending)
        {
            // Fee/net are unavailable while a capture is pending; re-read once before recording.
            try
            {
                capture = await _gateway.GetCaptureAsync(capture.CaptureId, ct);
            }
            catch (PaymentGatewayException)
            {
                // keep first read; operator can re-read via provider reporting
            }
        }

        order.RecordCapture(capture.CaptureId, capture.Status, capture.GrossAmount, capture.FeeAmount, capture.NetAmount, DateTimeOffset.UtcNow);
        await _unitOfWork.SaveChangesAsync(ct);

        _logger.LogInformation($"Order {order.Id} fulfilled: captured {capture.GrossAmount:0.00} {capture.Currency}, fee {capture.FeeAmount?.ToString("0.00", CultureInfo.InvariantCulture) ?? "n/a"}, net {capture.NetAmount?.ToString("0.00", CultureInfo.InvariantCulture) ?? "n/a"}.");
        return new FulfilResult(order, order.Payment!, Replayed: false);
    }

    public async Task<CancelResult> CancelAsync(int orderId, CancellationToken ct = default)
    {
        using var _ = await AsyncKeyedLock.LockAsync($"order:{orderId}", ct);

        var order = await LoadOrderAsync(orderId, ct);

        if (order.Status == OrderStatus.Cancelled)
        {
            return new CancelResult(order, FundsReleased: false, Replayed: true);
        }
        if (order.Status == OrderStatus.Fulfilled || order.Payment is { IsCaptured: true })
        {
            throw new OrderStateException("This order has already been fulfilled and the money was taken; issue a refund instead of cancelling.");
        }

        var fundsReleased = false;
        var payment = order.Payment;
        if (payment is not null)
        {
            if (payment.HasPendingAuthorizationToRecover)
            {
                payment = await SettlePendingAuthorizationAsync(order, payment, ct, quiet: true);
            }
            if (!string.IsNullOrEmpty(payment?.AuthorizationId) && !payment!.IsCaptured && !payment.IsVoided)
            {
                try
                {
                    await _gateway.VoidAsync(payment.AuthorizationId, ct);
                }
                catch (PaymentGatewayException ex) when (ex.Kind is PaymentFailureKind.ResourceNotFound or PaymentFailureKind.Conflict)
                {
                    // authorization already gone or already voided/captured on PayPal's side —
                    // the release request has done its job either way
                }
                fundsReleased = true;
            }
        }

        order.Cancel();
        await _unitOfWork.SaveChangesAsync(ct);

        _logger.LogInformation($"Order {order.Id} cancelled (held funds released: {fundsReleased}).");
        return new CancelResult(order, fundsReleased, Replayed: false);
    }

    public async Task<RefundResult> RefundAsync(int orderId, string callerId, bool callerIsAdmin, RefundCommand command, CancellationToken ct = default)
    {
        Guard.Against.NullOrEmpty(callerId, nameof(callerId));
        Guard.Against.NullOrEmpty(command?.IdempotencyKey, nameof(command.IdempotencyKey));

        using var _ = await AsyncKeyedLock.LockAsync($"order:{orderId}", ct);

        var order = await LoadOwnedOrderAsync(orderId, callerId, ct, callerIsAdmin);

        if (order.Status != OrderStatus.Fulfilled || order.Payment is not { IsCaptured: true })
        {
            throw new OrderStateException("Only fulfilled orders (money captured) can be refunded.");
        }

        var payment = order.Payment;
        var captureId = payment.CaptureId;
        if (string.IsNullOrEmpty(captureId))
        {
            throw new OrderStateException("The capture for this order cannot be located; the fulfilment state is incomplete.");
        }

        var existing = order.Refunds.FirstOrDefault(r => r.IdempotencyKey == command.IdempotencyKey);
        if (existing is not null)
        {
            if (command.Amount is null || RoundMoney(command.Amount.Value) == RoundMoney(existing.Amount))
            {
                return new RefundResult(order, existing, order.RemainingRefundableAmount(), Replayed: true);
            }
            throw new OrderStateException("This idempotency key was already used on the order with a different amount; use a new key for a distinct refund.");
        }

        var amount = command.Amount is null ? order.RemainingRefundableAmount() : RoundMoney(command.Amount.Value);
        if (amount <= 0m)
        {
            throw new OrderStateException("There is nothing left to refund on this order.");
        }
        if (amount > order.RemainingRefundableAmount())
        {
            throw new OrderStateException(
                $"The refund would exceed the captured amount. Captured: {payment.CapturedAmount:0.00}, already refunded: {order.RefundedAmount():0.00}, refundable: {order.RemainingRefundableAmount():0.00} {payment.CurrencyCode}.");
        }

        // Built on the hold's unique invoice reference (nonce included) so the merchant's
        // invoice-uniqueness rule is satisfied; the "-r-{key}" tail keeps the mapping from
        // this caller key deterministic for crash settlement below.
        var refundReference = RefundInvoiceReference(payment.InvoiceReference, command.IdempotencyKey);
        var settled = await FindRefundByReferenceAsync(payment.ProviderOrderId, SanitizeKey(command.IdempotencyKey), ct);
        if (settled is not null)
        {
            var recorded = new PaymentRefund(command.IdempotencyKey, settled.RefundId, captureId, settled.Amount, settled.Currency, settled.Status, settled.TotalRefundedAmount, settled.InvoiceReference ?? refundReference);
            order.AddRefund(recorded);
            await _unitOfWork.SaveChangesAsync(ct);
            return new RefundResult(order, recorded, order.RemainingRefundableAmount(), Replayed: true);
        }

        var refund = await _gateway.RefundAsync(captureId, amount, payment.CurrencyCode, refundReference, ct);

        var paymentRefund = new PaymentRefund(command.IdempotencyKey, refund.RefundId, captureId, refund.Amount, refund.Currency, refund.Status, refund.TotalRefundedAmount, refund.InvoiceReference ?? refundReference);
        order.AddRefund(paymentRefund);
        await _unitOfWork.SaveChangesAsync(ct);

        _logger.LogInformation($"Order {order.Id} refunded {refund.Amount:0.00} {refund.Currency} (refund {refund.RefundId}, status {refund.Status}).");
        return new RefundResult(order, paymentRefund, order.RemainingRefundableAmount(), Replayed: false);
    }

    public async Task<IReadOnlyList<Order>> GetOrdersForBuyerAsync(string buyerId, CancellationToken ct = default)
    {
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));
        var orders = await _orderRepository.ListAsync(new CustomerOrdersWithItemsSpecification(buyerId), ct);
        return orders.OrderByDescending(o => o.OrderDate).ToList();
    }

    public async Task<SavedPaymentMethod> SaveCardAsync(string buyerId, CardCredential card, CancellationToken ct = default)
    {
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));
        Guard.Against.Null(card, nameof(card));

        // Namespaced, deterministic per-shopper merchant customer id: it never collides with
        // other apps sharing the vault account and sidesteps provider-side corrupted customer
        // state found on shared demo ids (live: 500 for a raw "demouser@microsoft.com" mcid).
        var saved = await _gateway.VaultCardAsync(MerchantCustomerIdFor(buyerId), card, ct);

        var paymentMethod = new SavedPaymentMethod(
            Guid.NewGuid().ToString("N"),
            buyerId,
            saved.VaultCustomerId ?? string.Empty,
            saved.TokenId,
            saved.Brand ?? string.Empty,
            saved.Last4 ?? string.Empty,
            saved.Expiry ?? string.Empty,
            saved.CardholderName ?? card.CardholderName ?? string.Empty);

        paymentMethod = await _paymentMethodRepository.AddAsync(paymentMethod, ct);

        _logger.LogInformation($"Saved payment method {paymentMethod.ExternalId} created for buyer ({paymentMethod.Brand} ending {paymentMethod.Last4}).");
        return paymentMethod;
    }

    public async Task<IReadOnlyList<SavedPaymentMethod>> ListCardsAsync(string buyerId, CancellationToken ct = default)
    {
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));
        var cards = await _paymentMethodRepository.ListAsync(new SavedPaymentMethodsForBuyerSpecification(buyerId), ct);
        return cards.ToList();
    }

    public async Task DeleteCardAsync(string buyerId, string paymentMethodId, CancellationToken ct = default)
    {
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));

        var paymentMethod = await _paymentMethodRepository.FirstOrDefaultAsync(
            new SavedPaymentMethodByBuyerAndExternalIdSpecification(buyerId, paymentMethodId), ct);
        if (paymentMethod is null)
        {
            throw new PaymentMethodNotFoundException("Saved payment method not found.");
        }

        try
        {
            await _gateway.DeleteVaultCardAsync(paymentMethod.VaultTokenId, ct);
        }
        catch (PaymentGatewayException ex) when (ex.Kind == PaymentFailureKind.ResourceNotFound)
        {
            // already gone on the provider side — removing the local record completes the deletion
        }

        await _paymentMethodRepository.DeleteAsync(paymentMethod, ct);

        _logger.LogInformation($"Saved payment method {paymentMethod.ExternalId} deleted.");
    }

    // ---------- internals ----------

    private async Task<GatewayAuthorizeSource> BuildVaultSourceAsync(string buyerId, string paymentMethodId, CancellationToken ct)
    {
        var paymentMethod = await _paymentMethodRepository.FirstOrDefaultAsync(
            new SavedPaymentMethodByBuyerAndExternalIdSpecification(buyerId, paymentMethodId), ct);
        if (paymentMethod is null)
        {
            throw new PaymentMethodNotFoundException("Saved payment method not found.");
        }

        var previousNetworkTransactionReference = await FindPreviousNetworkTransactionReferenceAsync(buyerId, paymentMethod.VaultTokenId, ct);
        return new GatewayAuthorizeSource(null, paymentMethod.VaultTokenId, previousNetworkTransactionReference);
    }

    private async Task<string?> FindPreviousNetworkTransactionReferenceAsync(string buyerId, string vaultTokenId, CancellationToken ct)
    {
        var orders = await _orderRepository.ListAsync(new BuyerPaidOrdersSpecification(buyerId), ct);
        return orders.FirstOrDefault(o =>
            o.Payment is { } p &&
            p.UsedVaultTokenId == vaultTokenId &&
            !string.IsNullOrEmpty(p.NetworkTransactionReference))?.Payment!.NetworkTransactionReference;
    }

    private async Task<GatewayAuthorization> RecoverAuthorizationAsync(PaymentDetails pending, GatewayAuthorizeSource source, string currency, CancellationToken ct)
    {
        var snapshot = await _gateway.GetOrderSnapshotAsync(pending.ProviderOrderId, ct);
        var usable = snapshot?.Authorizations.FirstOrDefault(a => IsActive(a.Status) && (a.ExpirationTime is null || a.ExpirationTime > DateTimeOffset.UtcNow));
        if (usable is not null)
        {
            _logger.LogInformation($"Recovered existing authorization {usable.AuthorizationId} for provider order {pending.ProviderOrderId}.");
            return usable;
        }
        return await _gateway.AuthorizeExistingOrderAsync(pending.ProviderOrderId, source, ct);
    }

    private async Task<PaymentDetails> SettlePendingAuthorizationAsync(Order order, PaymentDetails payment, CancellationToken ct, bool quiet = false)
    {
        var snapshot = await _gateway.GetOrderSnapshotAsync(payment.ProviderOrderId, ct);
        var auth = snapshot?.Authorizations.OrderByDescending(a => a.CreatedTime).FirstOrDefault();
        if (auth is null || !IsActive(auth.Status))
        {
            if (quiet)
            {
                return payment;
            }
            throw new OrderStateException("The payment for this order never completed; there is nothing to act on. The shopper must pay again.");
        }
        payment.RenewAuthorization(auth.AuthorizationId, auth.Status, auth.Amount, auth.ExpirationTime);
        if (!quiet)
        {
            await _unitOfWork.SaveChangesAsync(ct);
        }
        return payment;
    }

    private async Task<PaymentDetails> RenewExpiredAuthorizationAsync(Order order, PaymentDetails payment, CancellationToken ct)
    {
        var createdAt = payment.AuthorizationCreatedTime ?? order.OrderDate;
        var ageInDays = (DateTimeOffset.UtcNow - createdAt).TotalDays;
        if (ageInDays > 30)
        {
            throw new OrderStateException(
                "The authorization has expired and is past PayPal's 30-day re-authorization window, so it cannot be renewed. " +
                "Release this hold (cancel the order) and ask the shopper to place and pay for a new order.");
        }

        try
        {
            var renewed = await _gateway.ReauthorizeAsync(payment.AuthorizationId, payment.AuthorizedAmount, payment.CurrencyCode, ct);
            payment.RenewAuthorization(renewed.AuthorizationId, renewed.Status, renewed.Amount, renewed.ExpirationTime);
            await _unitOfWork.SaveChangesAsync(ct);
            _logger.LogInformation($"Authorization on order {order.Id} renewed as {renewed.AuthorizationId}.");
            return payment;
        }
        catch (PaymentGatewayException ex) when (ex.Kind is PaymentFailureKind.ProviderRejected or PaymentFailureKind.Conflict)
        {
            if (string.IsNullOrEmpty(payment.UsedVaultTokenId))
            {
                throw new OrderStateException(
                    "The authorization expired and PayPal refused to renew it. The order was paid with a one-off card and no card data is kept, " +
                    "so the hold cannot be replaced automatically: cancel this order and ask the shopper to pay again.", ex);
            }

            var source = new GatewayAuthorizeSource(null, payment.UsedVaultTokenId,
                string.IsNullOrEmpty(payment.NetworkTransactionReference) ? null : payment.NetworkTransactionReference);
            var amount = RoundMoney(order.Total());
            var replacementInvoice = NewInvoiceReference(order.Id);
            var replacement = await _gateway.AuthorizeAsync(
                new GatewayAuthorizeRequest(amount, payment.CurrencyCode, replacementInvoice, OrderInvoiceReference(order.Id), source), ct);
            payment.NoteInvoiceReference(replacementInvoice);
            payment.RenewAuthorization(
                replacement.AuthorizationId, replacement.Status, replacement.Amount, replacement.ExpirationTime,
                replacement.ProviderOrderId, replacement.NetworkTransactionReference);
            await _unitOfWork.SaveChangesAsync(ct);
            _logger.LogWarning($"Authorization on order {order.Id} could not be re-authorized ({ex.Message}); a fresh hold {replacement.AuthorizationId} was placed on the saved card instead.");
            return payment;
        }
    }

    private async Task<GatewayCapture?> SettleCaptureFromProviderOrderAsync(Order order, PaymentGatewayException original, CancellationToken ct)
    {
        if (string.IsNullOrEmpty(order.Payment?.ProviderOrderId))
        {
            return null;
        }
        var snapshot = await _gateway.GetOrderSnapshotAsync(order.Payment.ProviderOrderId, ct);
        if (snapshot is null)
        {
            return null;
        }
        var capture = snapshot.Captures.FirstOrDefault(c => c.AuthorizationId == order.Payment.AuthorizationId)
                      ?? snapshot.Captures.FirstOrDefault();
        if (capture is null)
        {
            return null;
        }
        _logger.LogInformation($"Capture conflict on order {order.Id} settled from provider state: capture {capture.CaptureId}.");
        return capture;
    }

    private async Task<GatewayRefund?> FindRefundByReferenceAsync(string providerOrderId, string sanitizedKey, CancellationToken ct)
    {
        if (string.IsNullOrEmpty(providerOrderId))
        {
            return null;
        }
        try
        {
            var snapshot = await _gateway.GetOrderSnapshotAsync(providerOrderId, ct);
            return snapshot?.Refunds.FirstOrDefault(r =>
                r.InvoiceReference is { Length: > 0 } invoice &&
                invoice.EndsWith($"-r-{sanitizedKey}", StringComparison.Ordinal));
        }
        catch (PaymentGatewayException)
        {
            return null;
        }
    }

    private async Task<Order> LoadOrderAsync(int orderId, CancellationToken ct)
    {
        var order = await _orderRepository.GetByIdAsync(orderId, ct);
        if (order is null)
        {
            throw new OrderNotFoundException($"Order {orderId} not found.");
        }
        return order;
    }

    private async Task<Order> LoadOwnedOrderAsync(int orderId, string buyerId, CancellationToken ct, bool allowAdmin = false)
    {
        var order = await LoadOrderAsync(orderId, ct);
        if (!allowAdmin && order.BuyerId != buyerId)
        {
            // Do not reveal existence to non-owners.
            throw new OrderNotFoundException($"Order {orderId} not found.");
        }
        return order;
    }

    private string ResolveCurrency()
    {
        var currency = (_settings.Currency ?? string.Empty).Trim().ToUpperInvariant();
        if (currency.Length != 3 || !currency.All(char.IsLetter))
        {
            throw new ValidationFailureException("Payment currency is not configured correctly (PayPal:Currency must be an ISO-4217 code).");
        }
        return currency;
    }

    private static string OrderInvoiceReference(int orderId) => $"eshop-order-{orderId}";

    /// <summary>
    /// A fresh unique invoice reference for one hold attempt (the merchant account enforces
    /// invoice-id uniqueness, so bare "eshop-order-{id}" may already exist from an earlier run).
    /// </summary>
    private static string NewInvoiceReference(int orderId) =>
        $"{OrderInvoiceReference(orderId)}-{Guid.NewGuid().ToString("N")[..10]}";

    private static string RefundInvoiceReference(string holdInvoiceReference, string idempotencyKey) =>
        $"{holdInvoiceReference}-r-{SanitizeKey(idempotencyKey)}";

    private static string SanitizeKey(string idempotencyKey)
    {
        var key = new string(idempotencyKey.Where(c => char.IsLetterOrDigit(c) || c == '-').ToArray());
        if (key.Length > 40)
        {
            key = key[..40];
        }
        return key;
    }

    private static string MerchantCustomerIdFor(string buyerId)
    {
        var hash = System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(buyerId));
        return "eshop-" + Convert.ToHexString(hash)[..24].ToLowerInvariant();
    }

    private static decimal RoundMoney(decimal value) => Math.Round(value, 2, MidpointRounding.AwayFromZero);

    private static bool IsActive(string authorizationStatus) =>
        authorizationStatus is AuthorizationStatuses.Created or AuthorizationStatuses.Pending or AuthorizationStatuses.PartiallyCaptured;
}
