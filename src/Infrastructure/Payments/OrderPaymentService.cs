using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Ardalis.GuardClauses;
using Microsoft.EntityFrameworkCore;
using Microsoft.eShopWeb.ApplicationCore.Entities;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentMethodAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.PayPal;
using Microsoft.eShopWeb.ApplicationCore.Specifications;
using Microsoft.Extensions.Logging;

namespace Microsoft.eShopWeb.Infrastructure.Payments;

/// <summary>
/// Orchestrates an order's money movement over the existing <see cref="Order"/> aggregate:
/// place → pay (authorize hold) → fulfil (capture) / cancel (void) / refund. Idempotent in effect:
/// a double-click never authorizes or captures twice; a repeated refund under the same key is a
/// no-op.
/// </summary>
public sealed class OrderPaymentService : IOrderPaymentService
{
    // A stale-authorization guard: renew if the hold expires within this window before capture.
    private static readonly TimeSpan RenewalBuffer = TimeSpan.FromMinutes(2);

    private readonly IRepository<Order> _orderRepository;
    private readonly IReadRepository<CatalogItem> _catalogRepository;
    private readonly IReadRepository<SavedPaymentMethod> _savedCardRepository;
    private readonly IUriComposer _uriComposer;
    private readonly IPayPalGateway _gateway;
    private readonly ILogger<OrderPaymentService> _logger;

    public OrderPaymentService(
        IRepository<Order> orderRepository,
        IReadRepository<CatalogItem> catalogRepository,
        IReadRepository<SavedPaymentMethod> savedCardRepository,
        IUriComposer uriComposer,
        IPayPalGateway gateway,
        ILogger<OrderPaymentService> logger)
    {
        _orderRepository = orderRepository;
        _catalogRepository = catalogRepository;
        _savedCardRepository = savedCardRepository;
        _uriComposer = uriComposer;
        _gateway = gateway;
        _logger = logger;
    }

    public async Task<Order> PlaceOrderAsync(string buyerId, IReadOnlyCollection<OrderLine> lines,
        CancellationToken cancellationToken)
    {
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));
        if (lines is null || lines.Count == 0)
            throw new InvalidOrderPaymentStateException("An order must contain at least one item.");
        if (lines.Any(l => l.Quantity <= 0))
            throw new InvalidOrderPaymentStateException("Item quantities must be positive.");

        var ids = lines.Select(l => l.CatalogItemId).Distinct().ToArray();
        var catalogItems = await _catalogRepository.ListAsync(new CatalogItemsSpecification(ids), cancellationToken);

        var items = new List<OrderItem>();
        foreach (var line in lines)
        {
            var catalogItem = catalogItems.FirstOrDefault(c => c.Id == line.CatalogItemId)
                ?? throw new InvalidOrderPaymentStateException($"Catalog item {line.CatalogItemId} was not found.");
            var itemOrdered = new CatalogItemOrdered(catalogItem.Id, catalogItem.Name,
                _uriComposer.ComposePicUri(catalogItem.PictureUri));
            items.Add(new OrderItem(itemOrdered, catalogItem.Price, line.Quantity));
        }

        // No storefront address endpoint is in scope; the aggregate requires a ship-to address.
        var address = new Address("N/A", "N/A", "N/A", "N/A", "00000");
        var order = new Order(buyerId, address, items);
        await _orderRepository.AddAsync(order, cancellationToken);
        _logger.LogInformation("Placed order {OrderId} for {BuyerId} ({ItemCount} items, total {Total}).",
            order.Id, buyerId, items.Count, order.Total());
        return order;
    }

    public async Task<Order> PayAsync(int orderId, string buyerId, PayInstruction instruction,
        CancellationToken cancellationToken)
    {
        var order = await LoadOwnOrderAsync(orderId, buyerId, cancellationToken)
            ?? throw new OrderNotFoundException(orderId);

        // Idempotent: a double-click after a successful authorization is a no-op.
        if (order.Status == OrderStatus.Authorized) return order;
        if (order.Status != OrderStatus.AwaitingPayment)
            throw new InvalidOrderPaymentStateException(
                $"Order {orderId} cannot be paid from status {order.Status}.");

        // Resolve the payment source, scoped to the caller so one shopper cannot use another's card.
        string? vaultId = null;
        int? savedPaymentMethodId = null;
        if (instruction.SavedPaymentMethodId is int savedId)
        {
            var saved = (await _savedCardRepository.ListAsync(
                new SavedPaymentMethodByIdSpec(savedId, buyerId), cancellationToken)).FirstOrDefault()
                ?? throw new InvalidOrderPaymentStateException(
                    $"Saved card {savedId} was not found for this shopper.");
            vaultId = saved.PayPalPaymentTokenId;
            savedPaymentMethodId = saved.Id;
        }
        else if (instruction.Card is null)
        {
            throw new InvalidOrderPaymentStateException(
                "A payment requires either card details or a saved card id.");
        }

        var currency = _gateway.Currency;
        var reference = order.PaymentReference.ToString("N");
        var request = new PayPalAuthorizeRequest(
            OrderReference: OrderReference.For(orderId),
            InvoiceId: $"{OrderReference.For(orderId)}-{reference}",
            Currency: currency,
            Amount: order.Total(),
            Description: $"eShopOnWeb order {orderId}",
            IdempotencyKey: reference,
            Card: vaultId is null ? instruction.Card : null,
            VaultId: vaultId);

        var result = await _gateway.AuthorizeAsync(request, cancellationToken);

        order.RecordAuthorization(result.PayPalOrderId, result.AuthorizationId,
            result.AuthorizationStatus, result.ExpiresAt, currency, savedPaymentMethodId);

        try
        {
            await _orderRepository.UpdateAsync(order, cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            // A concurrent authorize won the race; return the persisted result.
            var reloaded = await LoadOwnOrderAsync(orderId, buyerId, cancellationToken);
            if (reloaded is { Status: OrderStatus.Authorized }) return reloaded;
            throw;
        }

        _logger.LogInformation("Authorized order {OrderId}: PayPal order {PayPalOrderId}, authorization {AuthId} ({Status}).",
            orderId, result.PayPalOrderId, result.AuthorizationId, result.AuthorizationStatus);
        return order;
    }

    public async Task<Order> FulfilAsync(int orderId, CancellationToken cancellationToken)
    {
        var order = await LoadAnyOrderAsync(orderId, cancellationToken)
            ?? throw new OrderNotFoundException(orderId);

        if (order.Status == OrderStatus.Fulfilled) return order; // idempotent
        if (order.Status != OrderStatus.Authorized || order.Payment?.AuthorizationId is null)
            throw new InvalidOrderPaymentStateException(
                $"Order {orderId} cannot be fulfilled from status {order.Status}.");

        // Renew a stale hold before capture rather than failing the fulfilment outright.
        if (IsAuthorizationStale(order.Payment))
            await RenewAuthorizationAsync(order, cancellationToken);

        var authorizationId = order.Payment!.AuthorizationId!;
        PayPalCaptureResult capture;
        try
        {
            capture = await _gateway.CaptureAsync(authorizationId, $"{order.PaymentReference:N}-capture", cancellationToken);
        }
        catch (PaymentGatewayException ex) when (ex.OutcomeUnknown)
        {
            // Connection/timeout: the capture may have landed. Settle by re-reading the PayPal order.
            var snapshot = await _gateway.GetOrderAsync(order.Payment!.PayPalOrderId, cancellationToken);
            if (snapshot?.Capture is { } settled)
            {
                capture = settled;
            }
            else
            {
                throw;
            }
        }
        catch (PaymentGatewayException ex) when (ex.StatusCode == 422 && !ex.OperatorActionable)
        {
            // The hold may have gone stale between our check and the capture — renew once and retry.
            _logger.LogWarning("Capture of order {OrderId} was rejected ({Status}); attempting to renew the authorization and retry.",
                orderId, ex.StatusCode);
            await RenewAuthorizationAsync(order, cancellationToken);
            capture = await _gateway.CaptureAsync(order.Payment!.AuthorizationId!, $"{order.PaymentReference:N}-capture", cancellationToken);
        }

        order.RecordCapture(capture.CaptureId, capture.Status, capture.Gross, capture.PaypalFee, capture.Net);
        await SaveWithConcurrencyRetryAsync(order, orderId, OrderStatus.Fulfilled, cancellationToken);

        _logger.LogInformation("Fulfilled order {OrderId}: captured {Gross} (fee {Fee}, net {Net}), capture {CaptureId}.",
            orderId, capture.Gross, capture.PaypalFee, capture.Net, capture.CaptureId);
        return order;
    }

    public async Task<Order> CancelAsync(int orderId, CancellationToken cancellationToken)
    {
        var order = await LoadAnyOrderAsync(orderId, cancellationToken)
            ?? throw new OrderNotFoundException(orderId);

        if (order.Status == OrderStatus.Cancelled) return order; // idempotent
        if (order.Status != OrderStatus.Authorized || order.Payment?.AuthorizationId is null)
            throw new InvalidOrderPaymentStateException(
                $"Order {orderId} cannot be cancelled from status {order.Status}.");

        try
        {
            await _gateway.VoidAsync(order.Payment!.AuthorizationId!, cancellationToken);
        }
        catch (PaymentGatewayException ex) when (ex.OutcomeUnknown)
        {
            var snap = await _gateway.GetAuthorizationAsync(order.Payment!.AuthorizationId!, cancellationToken);
            if (snap is null || !string.Equals(snap.Status, "VOIDED", StringComparison.OrdinalIgnoreCase))
                throw;
        }

        order.RecordCancellation();
        await SaveWithConcurrencyRetryAsync(order, orderId, OrderStatus.Cancelled, cancellationToken);
        _logger.LogInformation("Cancelled order {OrderId}; held funds released.", orderId);
        return order;
    }

    public async Task<OrderRefund> RefundAsync(int orderId, string buyerId, decimal? amount,
        string idempotencyKey, CancellationToken cancellationToken)
    {
        Guard.Against.NullOrEmpty(idempotencyKey, nameof(idempotencyKey));

        var order = await LoadOwnOrderAsync(orderId, buyerId, cancellationToken)
            ?? throw new OrderNotFoundException(orderId);

        // Idempotent: a repeated request under the same key returns the earlier refund.
        var existing = order.FindRefundByKey(idempotencyKey);
        if (existing is not null) return existing;

        if (order.Payment?.CaptureId is null)
            throw new InvalidOrderPaymentStateException(
                $"Order {orderId} has no captured payment to refund.");

        // Compute the effective amount and guard against over-refunding before any PayPal call.
        var effectiveAmount = amount ?? order.RefundableRemaining();
        order.GuardRefund(effectiveAmount);

        // The store dedups on the raw caller key (unique per order); the PayPal request id must be
        // unique per capture across runs, so compose it from the order's stable payment reference.
        var payPalRequestId = $"{order.PaymentReference:N}-{idempotencyKey}";
        PayPalRefundResult refundResult;
        try
        {
            refundResult = await _gateway.RefundAsync(order.Payment!.CaptureId!, effectiveAmount,
                order.Payment!.Currency, payPalRequestId, cancellationToken);
        }
        catch (PaymentGatewayException ex) when (ex.OutcomeUnknown)
        {
            // The refund may have landed. Re-issue with the SAME PayPal request id: PayPal dedups by
            // that key (45-day window) and returns the original refund if it did, or performs it if
            // it did not — settling the outcome rather than reporting a blind failure.
            _logger.LogWarning("Refund of order {OrderId} had an unknown outcome; re-issuing with the same key to settle.", orderId);
            refundResult = await _gateway.RefundAsync(order.Payment!.CaptureId!, effectiveAmount,
                order.Payment!.Currency, payPalRequestId, cancellationToken);
        }

        var refund = order.RecordRefund(idempotencyKey, refundResult.RefundId, refundResult.Amount,
            refundResult.Status);

        try
        {
            await _orderRepository.UpdateAsync(order, cancellationToken);
        }
        catch (DbUpdateException ex) when (IsUniqueViolation(ex))
        {
            // A concurrent request under the same key inserted first — return that refund.
            var reloaded = await LoadOwnOrderAsync(orderId, buyerId, cancellationToken);
            var already = reloaded?.FindRefundByKey(idempotencyKey);
            if (already is not null) return already;
            throw;
        }

        _logger.LogInformation("Refunded {Amount} against order {OrderId} (refund {RefundId}, key {Key}).",
            refundResult.Amount, orderId, refundResult.RefundId, idempotencyKey);
        return refund;
    }

    public async Task<IReadOnlyList<Order>> GetMyOrdersAsync(string buyerId, CancellationToken cancellationToken)
        => await _orderRepository.ListAsync(new OrdersWithPaymentByBuyerSpec(buyerId), cancellationToken);

    public Task<Order?> GetMyOrderAsync(int orderId, string buyerId, CancellationToken cancellationToken)
        => LoadOwnOrderAsync(orderId, buyerId, cancellationToken);

    // ---- helpers -------------------------------------------------------------------------

    private Task<Order?> LoadOwnOrderAsync(int orderId, string buyerId, CancellationToken ct)
        => _orderRepository.FirstOrDefaultAsync(new OrderWithPaymentByIdSpec(orderId, buyerId), ct);

    private Task<Order?> LoadAnyOrderAsync(int orderId, CancellationToken ct)
        => _orderRepository.FirstOrDefaultAsync(new OrderWithPaymentByIdSpec(orderId), ct);

    private static bool IsAuthorizationStale(OrderPayment payment)
        => payment.AuthorizationExpiresAt is DateTimeOffset exp
           && exp <= DateTimeOffset.UtcNow + RenewalBuffer;

    private async Task RenewAuthorizationAsync(Order order, CancellationToken cancellationToken)
    {
        var payment = order.Payment!;
        try
        {
            var renewed = await _gateway.ReauthorizeAsync(payment.AuthorizationId!, payment.Currency,
                payment.Amount, cancellationToken);
            order.RenewAuthorization(renewed.AuthorizationId, renewed.Status, renewed.ExpiresAt);
            await _orderRepository.UpdateAsync(order, cancellationToken);
            _logger.LogInformation("Renewed authorization for order {OrderId}: new authorization {AuthId}.",
                order.Id, renewed.AuthorizationId);
        }
        catch (PaymentGatewayException ex)
        {
            // The hold can no longer be renewed — surface something an operator can act on.
            throw new PaymentGatewayException(
                $"The payment authorization for order {order.Id} has expired and can no longer be renewed. " +
                "Ask the shopper to place and pay for the order again.",
                ex.StatusCode, ex.ProviderDebugId, outcomeUnknown: false, operatorActionable: true, ex);
        }
    }

    private async Task SaveWithConcurrencyRetryAsync(Order order, int orderId, OrderStatus expected,
        CancellationToken cancellationToken)
    {
        try
        {
            await _orderRepository.UpdateAsync(order, cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            var reloaded = await LoadAnyOrderAsync(orderId, cancellationToken);
            if (reloaded is not null && reloaded.Status == expected) return;
            throw;
        }
    }

    private static bool IsUniqueViolation(DbUpdateException ex)
    {
        // SQL Server unique-index violations surface as 2601/2627; fall back to a message probe for
        // other providers. (The in-memory provider does not enforce the index — see the plan.)
        var message = ex.InnerException?.Message ?? ex.Message;
        return message.Contains("2601") || message.Contains("2627")
            || message.IndexOf("duplicate", StringComparison.OrdinalIgnoreCase) >= 0
            || message.IndexOf("unique", StringComparison.OrdinalIgnoreCase) >= 0;
    }
}

/// <summary>The canonical eShop→PayPal order reference used as custom_id / invoice_id.</summary>
public static class OrderReference
{
    public const string Prefix = "ESHOP-";

    public static string For(int orderId) => $"{Prefix}{orderId}";

    public static bool TryParseOrderId(string? reference, out int orderId)
    {
        orderId = 0;
        if (string.IsNullOrEmpty(reference) || !reference.StartsWith(Prefix, StringComparison.Ordinal))
            return false;
        return int.TryParse(reference.AsSpan(Prefix.Length), out orderId);
    }
}
