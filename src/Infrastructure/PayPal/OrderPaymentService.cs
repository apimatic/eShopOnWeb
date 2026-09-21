using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.eShopWeb.ApplicationCore.Entities;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Specifications;
using Microsoft.Extensions.Logging;

namespace Microsoft.eShopWeb.Infrastructure.PayPal;

/// <summary>
/// Orchestrates the pay-for-an-order flow over the existing Order aggregate and PayPal. Persists a
/// local claim before every PayPal call, writes PayPal-owned ids/status after, and gates idempotent
/// transitions so a double-click never authorizes or captures twice.
/// </summary>
public class OrderPaymentService : IOrderPaymentService
{
    private readonly IRepository<Order> _orderRepository;
    private readonly IRepository<CatalogItem> _catalogRepository;
    private readonly IRepository<OrderPayment> _paymentRepository;
    private readonly IReadRepository<SavedCard> _savedCardRepository;
    private readonly IUriComposer _uriComposer;
    private readonly IPayPalGateway _gateway;
    private readonly PayPalOptions _options;
    private readonly ILogger<OrderPaymentService> _logger;

    public OrderPaymentService(
        IRepository<Order> orderRepository,
        IRepository<CatalogItem> catalogRepository,
        IRepository<OrderPayment> paymentRepository,
        IReadRepository<SavedCard> savedCardRepository,
        IUriComposer uriComposer,
        IPayPalGateway gateway,
        PayPalOptions options,
        ILogger<OrderPaymentService> logger)
    {
        _orderRepository = orderRepository;
        _catalogRepository = catalogRepository;
        _paymentRepository = paymentRepository;
        _savedCardRepository = savedCardRepository;
        _uriComposer = uriComposer;
        _gateway = gateway;
        _options = options;
        _logger = logger;
    }

    public async Task<int> PlaceOrderAsync(string buyerId, IReadOnlyList<OrderLineRequest> lines,
        ShippingAddressRequest? shipTo, CancellationToken ct)
    {
        if (lines is null || lines.Count == 0)
            throw new PaymentStateException("An order must contain at least one line item.");
        if (lines.Any(l => l.Quantity <= 0))
            throw new PaymentStateException("Every order line must have a quantity of at least 1.");

        var ids = lines.Select(l => l.CatalogItemId).Distinct().ToArray();
        var catalogItems = await _catalogRepository.ListAsync(new CatalogItemsSpecification(ids), ct);

        // Cross-operation invariant: every requested line must reference a real catalog item.
        var missing = ids.Where(id => catalogItems.All(c => c.Id != id)).ToArray();
        if (missing.Length > 0)
            throw new ResourceNotFoundException($"Unknown catalog item id(s): {string.Join(", ", missing)}.");

        var orderItems = lines.Select(line =>
        {
            var catalogItem = catalogItems.First(c => c.Id == line.CatalogItemId);
            var itemOrdered = new CatalogItemOrdered(catalogItem.Id, catalogItem.Name,
                _uriComposer.ComposePicUri(catalogItem.PictureUri));
            return new OrderItem(itemOrdered, catalogItem.Price, line.Quantity);
        }).ToList();

        var address = shipTo is null
            ? new Address("N/A", "N/A", "N/A", "US", "00000")
            : new Address(shipTo.Street, shipTo.City, shipTo.State, shipTo.Country, shipTo.ZipCode);

        var order = new Order(buyerId, address, orderItems);
        await _orderRepository.AddAsync(order, ct);
        _logger.LogInformation("Placed order {OrderId} for buyer with {LineCount} lines.", order.Id, orderItems.Count);
        return order.Id;
    }

    public async Task<OrderPaymentView> PayAsync(string buyerId, int orderId, CardDetails? card, int? savedCardId,
        CancellationToken ct)
    {
        var order = await LoadOwnedOrderAsync(buyerId, orderId, ct);

        if (card is null && savedCardId is null)
            throw new PaymentStateException("Provide card details or a saved-card id to pay.");
        if (card is not null && savedCardId is not null)
            throw new PaymentStateException("Provide either card details or a saved-card id, not both.");

        string? vaultId = null;
        if (savedCardId is not null)
        {
            // Cross-operation invariant: a saved card used to pay must be one this shopper owns.
            var savedCard = await _savedCardRepository.GetByIdAsync(savedCardId.Value, ct);
            if (savedCard is null || savedCard.BuyerId != buyerId)
                throw new ResourceNotFoundException($"Saved card {savedCardId} was not found for this shopper.");
            vaultId = savedCard.PayPalVaultId;
        }

        var payment = await _paymentRepository.FirstOrDefaultAsync(new OrderPaymentByOrderIdSpec(orderId), ct);

        // Idempotent replay: an order already authorized (or further along) is not authorized again.
        if (payment is not null && payment.Status is OrderPaymentStatus.Authorized or OrderPaymentStatus.Captured
            or OrderPaymentStatus.PartiallyRefunded or OrderPaymentStatus.Refunded or OrderPaymentStatus.Voided)
        {
            _logger.LogInformation("Pay for order {OrderId} is a no-op; payment already {Status}.", orderId, payment.Status);
            return ToView(payment);
        }

        // Unique per attempt, and the key both sides use to reconcile (well under PayPal's 127-char cap).
        var invoiceId = $"ESHOP-{orderId}-{Guid.NewGuid():N}";

        if (payment is null)
        {
            payment = new OrderPayment(orderId, buyerId, order.Total(), _options.Currency, invoiceId);
            try
            {
                // Local claim written BEFORE the PayPal call; the unique OrderId index rejects a
                // concurrent second pay.
                await _paymentRepository.AddAsync(payment, ct);
            }
            catch (DbUpdateException)
            {
                var existing = await _paymentRepository.FirstOrDefaultAsync(new OrderPaymentByOrderIdSpec(orderId), ct);
                if (existing is not null) return ToView(existing);
                throw;
            }
        }
        else
        {
            // Reuse a pending/failed row for a clean retry.
            payment.ResetForRetry(invoiceId);
            await _paymentRepository.UpdateAsync(payment, ct);
        }

        var request = new PayPalAuthorizeRequest(
            Amount: payment.Amount,
            CurrencyCode: payment.CurrencyCode,
            InvoiceId: payment.InvoiceId,
            OrderReference: orderId.ToString(CultureInfo.InvariantCulture),
            Description: $"eShopOnWeb order {orderId}",
            Card: card,
            VaultId: vaultId,
            // Idempotency keys are derived from the per-attempt invoiceId (a fresh GUID), so they are
            // unique across runs yet stable within one payment attempt — a concurrent double-click
            // reuses the same key and PayPal dedups it, while a genuine retry gets a fresh one.
            CreateRequestId: $"{payment.InvoiceId}-create",
            AuthorizeRequestId: $"{payment.InvoiceId}-auth");

        PayPalAuthorizationResult result;
        try
        {
            result = await _gateway.AuthorizeAsync(request, ct);
        }
        catch (Exception ex) when (ex is PayPalException or PaymentDeclinedException)
        {
            payment.SetFailed(ex.Message);
            await _paymentRepository.UpdateAsync(payment, ct);
            throw;
        }

        // Provider status branch: a hold is CREATED (PENDING is a held-but-pending state we surface);
        // anything else (DENIED/…) is a decline the shopper must act on.
        if (string.Equals(result.AuthorizationStatus, "DENIED", StringComparison.OrdinalIgnoreCase))
        {
            payment.SetFailed($"Authorization denied (status {result.AuthorizationStatus}).");
            await _paymentRepository.UpdateAsync(payment, ct);
            throw new PaymentDeclinedException(
                $"The card payment for order {orderId} was declined by PayPal.");
        }

        payment.SetAuthorized(result.PayPalOrderId, result.AuthorizationId, result.AuthorizationStatus,
            result.ExpiresAt, result.TransactionTime);
        await _paymentRepository.UpdateAsync(payment, ct);
        _logger.LogInformation("Authorized order {OrderId}: PayPal order {PayPalOrderId}, authorization {AuthId} ({Status}).",
            orderId, result.PayPalOrderId, result.AuthorizationId, result.AuthorizationStatus);
        return ToView(payment);
    }

    public async Task<OrderPaymentView> FulfilAsync(int orderId, CancellationToken ct)
    {
        var payment = await _paymentRepository.FirstOrDefaultAsync(new OrderPaymentByOrderIdSpec(orderId), ct)
            ?? throw new ResourceNotFoundException($"No payment found for order {orderId}; it cannot be fulfilled.");

        // No-op gating: an already-captured order is not captured again.
        if (payment.Status is OrderPaymentStatus.Captured or OrderPaymentStatus.PartiallyRefunded
            or OrderPaymentStatus.Refunded)
        {
            _logger.LogInformation("Fulfil for order {OrderId} is a no-op; already {Status}.", orderId, payment.Status);
            return ToView(payment);
        }

        if (payment.Status != OrderPaymentStatus.Authorized || payment.AuthorizationId is null)
            throw new PaymentStateException(
                $"Order {orderId} is not awaiting fulfilment (payment status {payment.Status}).");

        var capture = await CaptureWithRenewalAsync(payment, ct);

        // Provider status branch: COMPLETED/PENDING count as taken; DECLINED/FAILED do not.
        if (!(string.Equals(capture.Status, "COMPLETED", StringComparison.OrdinalIgnoreCase)
              || string.Equals(capture.Status, "PENDING", StringComparison.OrdinalIgnoreCase)))
        {
            throw new PaymentStateException(
                $"PayPal did not complete the capture for order {orderId} (status {capture.Status}).");
        }

        payment.SetCaptured(capture.CaptureId, capture.Status, capture.CapturedAmount,
            capture.PayPalFee, capture.NetAmount, capture.TransactionTime);
        await _paymentRepository.UpdateAsync(payment, ct);
        _logger.LogInformation("Captured order {OrderId}: capture {CaptureId} amount {Amount} fee {Fee} net {Net}.",
            orderId, capture.CaptureId, capture.CapturedAmount, capture.PayPalFee, capture.NetAmount);
        return ToView(payment);
    }

    public async Task<OrderPaymentView> CancelAsync(int orderId, CancellationToken ct)
    {
        var payment = await _paymentRepository.FirstOrDefaultAsync(new OrderPaymentByOrderIdSpec(orderId), ct)
            ?? throw new ResourceNotFoundException($"No payment found for order {orderId}; it cannot be cancelled.");

        if (payment.Status == OrderPaymentStatus.Voided)
            return ToView(payment);

        if (payment.Status != OrderPaymentStatus.Authorized || payment.AuthorizationId is null)
            throw new PaymentStateException(
                $"Order {orderId} cannot be cancelled in its current state (payment status {payment.Status}). " +
                "A captured order must be refunded, not cancelled.");

        await _gateway.VoidAsync(payment.AuthorizationId, $"{payment.InvoiceId}-void", ct);
        payment.SetVoided();
        await _paymentRepository.UpdateAsync(payment, ct);
        _logger.LogInformation("Cancelled order {OrderId}: authorization {AuthId} voided.", orderId, payment.AuthorizationId);
        return ToView(payment);
    }

    public async Task<RefundView> RefundAsync(string buyerId, int orderId, decimal? amount, string idempotencyKey,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(idempotencyKey))
            throw new PaymentStateException("A refund requires an idempotency key.");

        await LoadOwnedOrderAsync(buyerId, orderId, ct);

        var payment = await _paymentRepository.FirstOrDefaultAsync(new OrderPaymentByOrderIdSpec(orderId), ct)
            ?? throw new ResourceNotFoundException($"No payment found for order {orderId}.");

        if (payment.CaptureId is null || payment.Status is not (OrderPaymentStatus.Captured
                or OrderPaymentStatus.PartiallyRefunded or OrderPaymentStatus.Refunded))
            throw new PaymentStateException($"Order {orderId} has not been fulfilled; there is nothing to refund.");

        // Idempotent replay under the same key: return the existing refund without refunding again.
        var existing = payment.FindRefundByKey(idempotencyKey);
        if (existing is not null && existing.State != RefundState.Failed)
        {
            return ToRefundView(existing);
        }

        // Reserve against the captured amount BEFORE calling PayPal — this both enforces the
        // "never refundable beyond capture" rule and claims the idempotency key.
        PaymentRefund refund;
        try
        {
            refund = payment.ReserveRefund(idempotencyKey, amount);
        }
        catch (Exception ex) when (ex is InvalidOperationException or ArgumentException)
        {
            throw new PaymentStateException(ex.Message);
        }
        try
        {
            await _paymentRepository.UpdateAsync(payment, ct);
        }
        catch (DbUpdateException)
        {
            var reread = await _paymentRepository.FirstOrDefaultAsync(new OrderPaymentByOrderIdSpec(orderId), ct);
            var already = reread?.FindRefundByKey(idempotencyKey);
            if (already is not null && already.State != RefundState.Failed) return ToRefundView(already);
            throw;
        }

        try
        {
            var result = await _gateway.RefundAsync(payment.CaptureId, refund.Amount, payment.CurrencyCode,
                $"{payment.InvoiceId}-refund-{idempotencyKey}", ct);
            payment.ApplyRefundSuccess(refund, result.RefundId, result.Status);
            await _paymentRepository.UpdateAsync(payment, ct);
            _logger.LogInformation("Refunded order {OrderId}: refund {RefundId} amount {Amount} ({Status}).",
                orderId, result.RefundId, refund.Amount, result.Status);
            return ToRefundView(refund);
        }
        catch
        {
            // Release the reservation so the shopper can retry.
            payment.ReleaseRefund(refund);
            await _paymentRepository.UpdateAsync(payment, ct);
            throw;
        }
    }

    public async Task<IReadOnlyList<MyOrderView>> GetMyOrdersAsync(string buyerId, CancellationToken ct)
    {
        var orders = await _orderRepository.ListAsync(new CustomerOrdersWithItemsSpecification(buyerId), ct);
        if (orders.Count == 0) return Array.Empty<MyOrderView>();

        var payments = await _paymentRepository.ListAsync(
            new OrderPaymentsByOrderIdsSpec(orders.Select(o => o.Id)), ct);
        var paymentsByOrder = payments.ToDictionary(p => p.OrderId);

        return orders.Select(o =>
        {
            paymentsByOrder.TryGetValue(o.Id, out var payment);
            var items = o.OrderItems
                .Select(i => new MyOrderItemView(i.ItemOrdered.CatalogItemId, i.ItemOrdered.ProductName, i.UnitPrice, i.Units))
                .ToList();
            var status = payment is null ? "AwaitingPayment" : payment.Status.ToString();
            return new MyOrderView(o.Id, o.OrderDate, o.Total(), status, payment is null ? null : ToView(payment), items);
        }).ToList();
    }

    // ---------------- helpers ----------------

    private async Task<Order> LoadOwnedOrderAsync(string buyerId, int orderId, CancellationToken ct)
    {
        var order = await _orderRepository.FirstOrDefaultAsync(new OrderWithItemsByIdSpec(orderId), ct)
            ?? throw new ResourceNotFoundException($"Order {orderId} was not found.");
        if (!string.Equals(order.BuyerId, buyerId, StringComparison.Ordinal))
            throw new ForbiddenException($"Order {orderId} does not belong to the current shopper.");
        return order;
    }

    private async Task<PayPalCaptureResult> CaptureWithRenewalAsync(OrderPayment payment, CancellationToken ct)
    {
        var authorizationId = payment.AuthorizationId!;

        // Proactively renew an authorization that has already gone stale.
        if (payment.AuthorizationExpiresAt is { } expires && expires <= DateTimeOffset.UtcNow)
        {
            authorizationId = await RenewAuthorizationAsync(payment, ct);
        }

        try
        {
            return await _gateway.CaptureAsync(authorizationId, $"{payment.InvoiceId}-capture", ct);
        }
        catch (PayPalException ex) when (IsStaleAuthorization(ex))
        {
            _logger.LogWarning("Capture of order {OrderId} failed on a stale authorization; renewing.", payment.OrderId);
            authorizationId = await RenewAuthorizationAsync(payment, ct);
            return await _gateway.CaptureAsync(authorizationId, $"{payment.InvoiceId}-recapture", ct);
        }
    }

    private async Task<string> RenewAuthorizationAsync(OrderPayment payment, CancellationToken ct)
    {
        try
        {
            var info = await _gateway.ReauthorizeAsync(payment.AuthorizationId!, $"{payment.InvoiceId}-reauth", ct);
            payment.RenewAuthorization(info.AuthorizationId, info.Status, info.ExpiresAt);
            await _paymentRepository.UpdateAsync(payment, ct);
            return info.AuthorizationId;
        }
        catch (PayPalException ex)
        {
            throw new PaymentStateException(
                $"The authorization for order {payment.OrderId} has expired and can no longer be renewed ({ex.Message}). " +
                "Ask the shopper to place and pay for the order again.");
        }
    }

    private static bool IsStaleAuthorization(PayPalException ex)
    {
        var name = ex.ProviderErrorName ?? string.Empty;
        var message = ex.Message ?? string.Empty;
        return name.IndexOf("EXPIR", StringComparison.OrdinalIgnoreCase) >= 0
            || message.IndexOf("EXPIR", StringComparison.OrdinalIgnoreCase) >= 0
            || message.IndexOf("AUTHORIZATION", StringComparison.OrdinalIgnoreCase) >= 0;
    }

    internal static OrderPaymentView ToView(OrderPayment p) => new(
        OrderId: p.OrderId,
        Status: p.Status.ToString(),
        Amount: p.Amount,
        CurrencyCode: p.CurrencyCode,
        PayPalOrderId: p.PayPalOrderId,
        AuthorizationId: p.AuthorizationId,
        AuthorizationStatus: p.AuthorizationStatus,
        AuthorizationExpiresAt: p.AuthorizationExpiresAt,
        CaptureId: p.CaptureId,
        CaptureStatus: p.CaptureStatus,
        CapturedAmount: p.CapturedAmount,
        PayPalFee: p.PayPalFee,
        NetAmount: p.NetAmount,
        RefundedAmount: p.RefundedAmount,
        Refunds: p.Refunds.Select(ToRefundView).ToList());

    private static RefundView ToRefundView(PaymentRefund r) =>
        new(r.Id, r.PayPalRefundId, r.Amount, r.State.ToString());
}
