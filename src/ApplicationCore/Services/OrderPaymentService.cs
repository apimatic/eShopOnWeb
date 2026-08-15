using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Ardalis.GuardClauses;
using Microsoft.eShopWeb.ApplicationCore.Configuration;
using Microsoft.eShopWeb.ApplicationCore.Entities;
using Microsoft.eShopWeb.ApplicationCore.Entities.BuyerAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Specifications;

namespace Microsoft.eShopWeb.ApplicationCore.Services;

/// <summary>
/// Drives the order money movement end to end against PayPal while keeping eShop's own order state
/// authoritative for ownership and lifecycle. Each public operation is idempotent in effect: it
/// checks the stored PayPal state first and re-uses it rather than calling PayPal a second time,
/// and it also sends a deterministic PayPal-Request-Id so PayPal itself de-duplicates retries.
/// </summary>
public class OrderPaymentService : IOrderPaymentService
{
    private readonly IRepository<Order> _orderRepository;
    private readonly IRepository<Buyer> _buyerRepository;
    private readonly IRepository<CatalogItem> _itemRepository;
    private readonly IPayPalClient _payPal;
    private readonly IUriComposer _uriComposer;
    private readonly PayPalSettings _settings;
    private readonly IAppLogger<OrderPaymentService> _logger;

    public OrderPaymentService(
        IRepository<Order> orderRepository,
        IRepository<Buyer> buyerRepository,
        IRepository<CatalogItem> itemRepository,
        IPayPalClient payPal,
        IUriComposer uriComposer,
        PayPalSettings settings,
        IAppLogger<OrderPaymentService> logger)
    {
        _orderRepository = orderRepository;
        _buyerRepository = buyerRepository;
        _itemRepository = itemRepository;
        _payPal = payPal;
        _uriComposer = uriComposer;
        _settings = settings;
        _logger = logger;
    }

    private string Currency => _settings.Currency;

    public async Task<Order> PlaceOrderAsync(string buyerId, IReadOnlyCollection<OrderLineRequest> lines,
        CancellationToken ct = default)
    {
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));
        if (lines is null || lines.Count == 0)
        {
            throw new PaymentException("An order must contain at least one line.");
        }
        foreach (var line in lines)
        {
            if (line.Quantity <= 0)
            {
                throw new PaymentException($"Quantity for catalog item {line.CatalogItemId} must be greater than zero.");
            }
        }

        var ids = lines.Select(l => l.CatalogItemId).Distinct().ToArray();
        var catalogItems = await _itemRepository.ListAsync(new CatalogItemsSpecification(ids), ct);
        var missing = ids.Where(id => catalogItems.All(c => c.Id != id)).ToArray();
        if (missing.Length > 0)
        {
            throw new EntityNotFoundException($"Catalog item(s) not found: {string.Join(", ", missing)}.");
        }

        var items = lines.Select(line =>
        {
            var catalogItem = catalogItems.First(c => c.Id == line.CatalogItemId);
            var itemOrdered = new CatalogItemOrdered(catalogItem.Id, catalogItem.Name,
                _uriComposer.ComposePicUri(catalogItem.PictureUri));
            return new OrderItem(itemOrdered, catalogItem.Price, line.Quantity);
        }).ToList();

        // eShop's reference app ships to a fixed address; the payment flow is additive and does not
        // change that. Amounts always come from catalog prices (snapshotted above), never the caller.
        var order = new Order(buyerId, new Address("123 Main St.", "Kent", "OH", "United States", "44240"), items);
        order = await _orderRepository.AddAsync(order, ct);
        _logger.LogInformation("Placed order {0} for buyer {1}; total {2} {3}.",
            order.Id, buyerId, Format(order.Total()), Currency);
        return order;
    }

    public async Task<Order> PayAsync(int orderId, string buyerId, PaymentInstrument instrument,
        CancellationToken ct = default)
    {
        var order = await LoadOwnedOrderAsync(orderId, buyerId, ct);

        if (order.Status == OrderStatus.Cancelled)
        {
            throw new PaymentException("This order has been cancelled and can no longer be paid.");
        }
        if (order.Status == OrderStatus.Fulfilled)
        {
            throw new PaymentException("This order has already been fulfilled.");
        }

        // Idempotency: if the order is already authorized, return it unchanged.
        if (order.Payment?.IsAuthorized == true
            && string.Equals(order.Payment.AuthorizationStatus, "CREATED", StringComparison.OrdinalIgnoreCase))
        {
            _logger.LogInformation("Order {0} is already authorized ({1}); skipping re-authorization.",
                orderId, order.Payment.AuthorizationId);
            return order;
        }

        var amount = order.Total();
        // The invoice id is generated once when the payment starts and then persisted, so retries reuse
        // it. It embeds the eShop order id (for reconciliation matching) plus a unique suffix, because
        // the merchant account requires a globally-unique invoice id per transaction.
        var payment = order.StartPayment(Currency, BuildInvoiceId(order));
        var invoiceId = payment.InvoiceId;

        // Resolve a saved card to its vault token, scoped strictly to the caller.
        string? vaultTokenId = null;
        if (instrument.UsesSavedCard)
        {
            vaultTokenId = await ResolveSavedCardTokenAsync(buyerId, instrument.SavedPaymentMethodId!.Value, ct);
        }
        else if (instrument.Card is null)
        {
            throw new PaymentException("Provide either card details or a saved payment method id to pay.");
        }

        // 1) Create the PayPal order (intent=AUTHORIZE) if we have not already.
        if (string.IsNullOrEmpty(payment.PayPalOrderId))
        {
            var created = await _payPal.CreateAuthorizeOrderAsync(amount, Currency, invoiceId,
                requestId: $"create-{invoiceId}", ct);
            payment.SetPayPalOrderId(created.Id);
            await _orderRepository.UpdateAsync(order, ct); // persist the PayPal order id before authorizing
        }

        // 2) Authorize (hold) the funds for exactly the order total. Request ids are derived from the
        // globally-unique invoice id (not the recyclable order id) so PayPal does not replay a cached
        // result from a previous run after an in-memory database reset.
        PayPalAuthorizationResult auth;
        try
        {
            auth = vaultTokenId is not null
                ? await _payPal.AuthorizeOrderWithVaultAsync(payment.PayPalOrderId!, vaultTokenId,
                    requestId: $"auth-{invoiceId}", ct)
                : await _payPal.AuthorizeOrderWithCardAsync(payment.PayPalOrderId!, instrument.Card!,
                    requestId: $"auth-{invoiceId}", ct);
        }
        catch (PayPalApiException ex)
        {
            throw new PaymentException($"The card could not be authorized: {ex.DescribeIssues()}", ex);
        }

        if (string.IsNullOrEmpty(auth.AuthorizationId))
        {
            throw new PaymentException(
                $"PayPal did not return an authorization for order {order.Id} (order status {auth.OrderStatus}). " +
                "The payment may require additional shopper action, which this flow does not support.");
        }

        payment.SetAuthorization(auth.AuthorizationId!, auth.Status ?? "CREATED", auth.ExpiresAt,
            auth.CardBrand, auth.CardLast4, vaultTokenId);
        order.MarkAuthorized();
        await _orderRepository.UpdateAsync(order, ct);

        _logger.LogInformation("Authorized {0} {1} for order {2} (auth {3}, expires {4}).",
            Format(amount), Currency, order.Id, auth.AuthorizationId, auth.ExpiresAt);
        return order;
    }

    public async Task<Order> FulfilAsync(int orderId, CancellationToken ct = default)
    {
        var order = await LoadOrderAsync(orderId, ct);
        var payment = order.Payment
            ?? throw new PaymentException("This order has no payment to capture; it has not been paid.");

        if (order.Status == OrderStatus.Cancelled)
        {
            throw new PaymentException("A cancelled order cannot be fulfilled.");
        }

        // Idempotency: already captured → return as-is.
        if (payment.IsCaptured)
        {
            _logger.LogInformation("Order {0} is already fulfilled (capture {1}).", orderId, payment.CaptureId);
            return order;
        }
        if (!payment.IsAuthorized)
        {
            throw new PaymentException("This order has not been authorized; it cannot be fulfilled.");
        }

        var amount = payment.AuthorizedAmount;
        var authorizationId = await EnsureCapturableAuthorizationAsync(order, ct);

        PayPalCaptureResult capture;
        try
        {
            capture = await _payPal.CaptureAuthorizationAsync(authorizationId, amount, Currency,
                requestId: $"capture-{authorizationId}", ct);
        }
        catch (PayPalApiException ex) when (IsExpiredAuthorization(ex))
        {
            // The hold went stale between our check and the capture: renew once and retry.
            _logger.LogWarning("Authorization {0} for order {1} expired at capture; renewing.",
                authorizationId, order.Id);
            authorizationId = await ReauthorizeAsync(order, payment, ct);
            capture = await _payPal.CaptureAuthorizationAsync(authorizationId, amount, Currency,
                requestId: $"capture-{authorizationId}", ct);
        }

        payment.SetCapture(capture.CaptureId, capture.Status, capture.GrossAmount, capture.PayPalFee, capture.NetAmount);
        order.MarkFulfilled();
        await _orderRepository.UpdateAsync(order, ct);

        _logger.LogInformation("Fulfilled order {0}: captured {1} {2} (fee {3}, net {4}), capture {5}.",
            order.Id, Format(capture.GrossAmount), capture.Currency, Format(capture.PayPalFee ?? 0m),
            Format(capture.NetAmount ?? 0m), capture.CaptureId);
        return order;
    }

    public async Task<Order> CancelAsync(int orderId, CancellationToken ct = default)
    {
        var order = await LoadOrderAsync(orderId, ct);

        if (order.Status == OrderStatus.Fulfilled)
        {
            throw new PaymentException("This order has already been fulfilled; refund it instead of cancelling.");
        }

        // Idempotency: cancelling an already-cancelled order is a no-op.
        if (order.Status == OrderStatus.Cancelled)
        {
            return order;
        }

        var payment = order.Payment;
        if (payment is { IsAuthorized: true, IsCaptured: false }
            && !string.Equals(payment.AuthorizationStatus, "VOIDED", StringComparison.OrdinalIgnoreCase))
        {
            try
            {
                await _payPal.VoidAuthorizationAsync(payment.AuthorizationId!, requestId: $"void-{payment.AuthorizationId}", ct);
            }
            catch (PayPalApiException ex) when (IsExpiredAuthorization(ex) || ex.StatusCode == 404 || ex.HasIssue("AUTH_ALREADY_VOIDED"))
            {
                // Already released or expired on PayPal's side; the funds are not held regardless.
                _logger.LogWarning("Void of authorization {0} for order {1} was unnecessary: {2}.",
                    payment.AuthorizationId, order.Id, ex.DescribeIssues());
            }
            payment.MarkVoided();
        }

        order.MarkCancelled();
        await _orderRepository.UpdateAsync(order, ct);
        _logger.LogInformation("Cancelled order {0}; any held funds released.", order.Id);
        return order;
    }

    public async Task<PaymentRefund> RefundAsync(int orderId, string buyerId, decimal? amount, string idempotencyKey,
        CancellationToken ct = default)
    {
        Guard.Against.NullOrEmpty(idempotencyKey, nameof(idempotencyKey));
        var order = await LoadOwnedOrderAsync(orderId, buyerId, ct);
        var payment = order.Payment;

        if (payment is null || !payment.IsCaptured || payment.CaptureId is null)
        {
            throw new PaymentException("This order has not been captured; there is nothing to refund.");
        }

        // Idempotency: a repeat under the same key returns the original refund without refunding again.
        var existing = payment.FindRefundByKey(idempotencyKey);
        if (existing is not null)
        {
            _logger.LogInformation("Refund for order {0} under key {1} already processed (refund {2}).",
                order.Id, idempotencyKey, existing.PayPalRefundId);
            return existing;
        }

        var refundAmount = amount ?? payment.RefundableRemaining();
        if (refundAmount <= 0m)
        {
            throw new PaymentException("Refund amount must be greater than zero.");
        }
        // A partly-refunded order must never become refundable beyond what was captured.
        if (refundAmount > payment.RefundableRemaining())
        {
            throw new PaymentException(
                $"Refund amount {Format(refundAmount)} {Currency} exceeds the refundable remaining " +
                $"{Format(payment.RefundableRemaining())} {Currency} on this capture.");
        }

        var refund = payment.AddRefund(idempotencyKey, refundAmount);
        // Persist the intent before calling PayPal so a crash can't double-refund on retry.
        await _orderRepository.UpdateAsync(order, ct);

        PayPalRefundResult result;
        try
        {
            result = await _payPal.RefundCaptureAsync(payment.CaptureId, amount, Currency, idempotencyKey, ct);
        }
        catch (PayPalApiException ex)
        {
            throw new PaymentException($"The refund could not be processed: {ex.DescribeIssues()}", ex);
        }

        refund.SetResult(result.RefundId, result.Status);
        await _orderRepository.UpdateAsync(order, ct);

        _logger.LogInformation("Refunded {0} {1} on order {2} (refund {3}, status {4}).",
            Format(result.Amount), result.Currency, order.Id, result.RefundId, result.Status);
        return refund;
    }

    // ---- helpers ----

    /// <summary>Ensures we hold a capturable authorization, renewing a stale one rather than failing the
    /// fulfilment outright. Throws an operator-actionable error when it can no longer be renewed.</summary>
    private async Task<string> EnsureCapturableAuthorizationAsync(Order order, CancellationToken ct)
    {
        var payment = order.Payment!;
        var authorizationId = payment.AuthorizationId!;

        PayPalAuthorizationResult current;
        try
        {
            current = await _payPal.GetAuthorizationAsync(authorizationId, ct);
        }
        catch (PayPalApiException ex) when (ex.StatusCode == 404)
        {
            throw new PaymentException(
                "The payment authorization no longer exists at PayPal and cannot be renewed. " +
                "Ask the shopper to pay for this order again.", ex);
        }

        var stale = IsStale(current);
        if (!stale)
        {
            return authorizationId;
        }

        _logger.LogWarning("Authorization {0} for order {1} is stale (status {2}, expires {3}); renewing.",
            authorizationId, order.Id, current.Status, current.ExpiresAt);
        return await ReauthorizeAsync(order, payment, ct);
    }

    private async Task<string> ReauthorizeAsync(Order order, OrderPayment payment, CancellationToken ct)
    {
        try
        {
            var reauth = await _payPal.ReauthorizeAsync(payment.AuthorizationId!, payment.AuthorizedAmount, Currency,
                requestId: $"reauth-{payment.AuthorizationId}", ct);
            if (string.IsNullOrEmpty(reauth.AuthorizationId))
            {
                throw new PaymentException(
                    "The payment authorization could not be renewed. Ask the shopper to pay for this order again.");
            }
            payment.SetAuthorization(reauth.AuthorizationId!, reauth.Status ?? "CREATED", reauth.ExpiresAt,
                payment.CardBrand, payment.CardLast4, payment.VaultTokenIdUsed);
            await _orderRepository.UpdateAsync(order, ct);
            return reauth.AuthorizationId!;
        }
        catch (PayPalApiException ex)
        {
            throw new PaymentException(
                "The payment authorization has expired and can no longer be renewed " +
                $"({ex.DescribeIssues()}). Ask the shopper to pay for this order again.", ex);
        }
    }

    private static bool IsStale(PayPalAuthorizationResult auth)
    {
        if (!string.Equals(auth.Status, "CREATED", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }
        // Renew slightly ahead of the honor-period expiry to avoid capturing against a just-expired hold.
        return auth.ExpiresAt.HasValue && auth.ExpiresAt.Value <= DateTimeOffset.UtcNow.AddMinutes(5);
    }

    private static bool IsExpiredAuthorization(PayPalApiException ex) =>
        ex.HasIssue("AUTHORIZATION_EXPIRED") || ex.HasIssue("AUTH_CAPTURE_LIMIT_EXCEEDED")
        || ex.HasIssue("MAX_CAPTURE_COUNT_EXCEEDED") || ex.HasIssue("AUTHORIZATION_VOIDED");

    private async Task<string> ResolveSavedCardTokenAsync(string buyerId, int paymentMethodId, CancellationToken ct)
    {
        var buyer = await _buyerRepository.FirstOrDefaultAsync(new BuyerWithPaymentMethodsSpec(buyerId), ct)
            ?? throw new EntityNotFoundException($"Saved card {paymentMethodId} was not found.");
        var method = buyer.FindPaymentMethod(paymentMethodId);
        if (method?.VaultTokenId is null)
        {
            throw new EntityNotFoundException($"Saved card {paymentMethodId} was not found.");
        }
        return method.VaultTokenId;
    }

    private async Task<Order> LoadOrderAsync(int orderId, CancellationToken ct)
    {
        return await _orderRepository.FirstOrDefaultAsync(new OrderWithPaymentByIdSpec(orderId), ct)
            ?? throw new EntityNotFoundException($"Order {orderId} was not found.");
    }

    private async Task<Order> LoadOwnedOrderAsync(int orderId, string buyerId, CancellationToken ct)
    {
        var order = await LoadOrderAsync(orderId, ct);
        // One shopper must never see or act on another's order; do not distinguish not-found from not-yours.
        if (!string.Equals(order.BuyerId, buyerId, StringComparison.Ordinal))
        {
            throw new EntityNotFoundException($"Order {orderId} was not found.");
        }
        return order;
    }

    /// <summary>A stable, eShop-owned reference sent to PayPal as the invoice id, used to line PayPal's
    /// transaction report back up against this order during reconciliation. It carries the order id for
    /// matching and a unique suffix so it is globally unique per transaction (a merchant-account
    /// requirement), and survives in-memory database resets that recycle order ids across runs.</summary>
    private static string BuildInvoiceId(Order order) => $"ESHOP-{order.Id}-{Guid.NewGuid():N}";

    private static string Format(decimal amount) => amount.ToString("0.00", CultureInfo.InvariantCulture);
}
