using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Interfaces.Payments;
using Microsoft.eShopWeb.ApplicationCore.Specifications;
using Microsoft.eShopWeb.ApplicationCore.Specifications.Payments;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Microsoft.eShopWeb.Infrastructure.Payments;

/// <summary>
/// Orchestrates pay / fulfil / cancel / refund over the existing <see cref="Order"/> model and the
/// PayPal gateway. Persists a local claim before every PayPal write, gates repeated operations on
/// actual state change, and re-reads the provider on an ambiguous outcome.
/// </summary>
public class OrderPaymentService : IOrderPaymentService
{
    private readonly IRepository<Order> _orderRepository;
    private readonly IRepository<OrderPayment> _paymentRepository;
    private readonly IRepository<OrderRefund> _refundRepository;
    private readonly IRepository<CatalogItem> _catalogRepository;
    private readonly IRepository<SavedPaymentMethod> _savedCardRepository;
    private readonly IPayPalPaymentGateway _gateway;
    private readonly IPaymentConfiguration _config;
    private readonly IUriComposer _uriComposer;
    private readonly ILogger<OrderPaymentService> _logger;

    public OrderPaymentService(
        IRepository<Order> orderRepository,
        IRepository<OrderPayment> paymentRepository,
        IRepository<OrderRefund> refundRepository,
        IRepository<CatalogItem> catalogRepository,
        IRepository<SavedPaymentMethod> savedCardRepository,
        IPayPalPaymentGateway gateway,
        IPaymentConfiguration config,
        IUriComposer uriComposer,
        ILogger<OrderPaymentService> logger)
    {
        _orderRepository = orderRepository;
        _paymentRepository = paymentRepository;
        _refundRepository = refundRepository;
        _catalogRepository = catalogRepository;
        _savedCardRepository = savedCardRepository;
        _gateway = gateway;
        _config = config;
        _uriComposer = uriComposer;
        _logger = logger;
    }

    public async Task<int> PlaceOrderAsync(string buyerId, IReadOnlyList<PlaceOrderItem> items,
        ShippingAddressInput? shipTo, CancellationToken ct)
    {
        if (items is null || items.Count == 0)
            throw new PaymentFlowException(PaymentFlowError.Validation, "At least one order item is required.");
        if (items.Any(i => i.Quantity <= 0))
            throw new PaymentFlowException(PaymentFlowError.Validation, "Every order item quantity must be greater than zero.");

        var ids = items.Select(i => i.CatalogItemId).Distinct().ToArray();
        var catalogItems = await _catalogRepository.ListAsync(new CatalogItemsSpecification(ids), ct);

        var orderItems = new List<OrderItem>();
        foreach (var line in items)
        {
            var catalogItem = catalogItems.FirstOrDefault(c => c.Id == line.CatalogItemId)
                ?? throw new PaymentFlowException(PaymentFlowError.Validation,
                    $"Catalog item {line.CatalogItemId} does not exist.");
            var ordered = new CatalogItemOrdered(catalogItem.Id, catalogItem.Name,
                _uriComposer.ComposePicUri(catalogItem.PictureUri));
            orderItems.Add(new OrderItem(ordered, catalogItem.Price, line.Quantity));
        }

        var address = BuildAddress(shipTo);
        var order = new Order(buyerId, address, orderItems);
        await _orderRepository.AddAsync(order, ct);

        _logger.LogInformation("Order {OrderId} placed by {BuyerId} ({ItemCount} items).",
            order.Id, buyerId, orderItems.Count);
        return order.Id;
    }

    public async Task<PaymentView> PayAsync(string buyerId, int orderId, CardDetails? card,
        Guid? savedPaymentMethodId, CancellationToken ct)
    {
        if (card is null && savedPaymentMethodId is null)
            throw new PaymentFlowException(PaymentFlowError.Validation,
                "Provide either card details or a saved paymentMethodId to pay with.");
        if (card is not null && savedPaymentMethodId is not null)
            throw new PaymentFlowException(PaymentFlowError.Validation,
                "Provide either card details or a saved paymentMethodId, not both.");

        var order = await _orderRepository.FirstOrDefaultAsync(new OrderWithItemsByIdSpec(orderId), ct);
        if (order is null || order.BuyerId != buyerId)
            throw new PaymentFlowException(PaymentFlowError.NotFound, $"Order {orderId} was not found.");

        string? vaultId = null;
        if (savedPaymentMethodId is not null)
        {
            var saved = await _savedCardRepository.FirstOrDefaultAsync(
                new SavedPaymentMethodByIdSpecification(savedPaymentMethodId.Value, buyerId), ct);
            if (saved is null)
                throw new PaymentFlowException(PaymentFlowError.Validation, "The saved card was not found.");
            vaultId = saved.PayPalVaultId;
        }

        var currency = _config.Currency;
        var total = order.Total();
        if (total <= 0m)
            throw new PaymentFlowException(PaymentFlowError.Validation, "The order total must be greater than zero.");

        // Idempotent-return for a payment already made; retry only from a failed attempt.
        var existing = await _paymentRepository.GetByIdAsync(orderId, ct);
        if (existing is not null)
        {
            switch (existing.Status)
            {
                case PaymentStatus.Authorized:
                case PaymentStatus.Fulfilled:
                case PaymentStatus.PartiallyRefunded:
                case PaymentStatus.Refunded:
                    return BuildView(existing);
                case PaymentStatus.Authorizing:
                    throw new PaymentFlowException(PaymentFlowError.Conflict,
                        "A payment for this order is already in progress.");
                case PaymentStatus.Cancelled:
                    throw new PaymentFlowException(PaymentFlowError.Conflict,
                        "This order was cancelled and cannot be paid again.");
                case PaymentStatus.Failed:
                    // Release the stale failed claim so a fresh attempt can re-claim the order.
                    await _paymentRepository.DeleteAsync(existing, ct);
                    break;
            }
        }

        var invoiceId = $"ESHOP-{orderId}-{Guid.NewGuid():N}".Substring(0, Math.Min(40, 7 + orderId.ToString().Length + 33));
        var payment = new OrderPayment(orderId, buyerId, currency, total, invoiceId, savedPaymentMethodId);

        // Write the local claim BEFORE the PayPal call. The primary-key uniqueness on OrderId rejects a
        // concurrent second attempt; we catch that and return the winner's state.
        try
        {
            await _paymentRepository.AddAsync(payment, ct);
        }
        catch (Exception ex) when (IsUniqueViolation(ex))
        {
            _logger.LogInformation("Concurrent pay for order {OrderId} lost the claim; returning existing state.", orderId);
            var winner = await _paymentRepository.GetByIdAsync(orderId, ct);
            if (winner is not null) return BuildView(winner);
            throw new PaymentFlowException(PaymentFlowError.Conflict, "A payment for this order is already in progress.");
        }

        try
        {
            var result = await _gateway.AuthorizeAsync(
                new AuthorizePaymentRequest(orderId, total, currency, invoiceId, orderId.ToString(CultureInfo.InvariantCulture),
                    $"eShopOnWeb order {orderId}", card, vaultId), ct);

            payment.MarkAuthorized(result.PayPalOrderId, result.AuthorizationId, result.ExpiresAt);
            await _paymentRepository.UpdateAsync(payment, ct);
            _logger.LogInformation("Order {OrderId} authorized: paypalOrder={PayPalOrder} auth={AuthId} status={Status}.",
                orderId, result.PayPalOrderId, result.AuthorizationId, result.Status);
            return BuildView(payment);
        }
        catch (PayPalGatewayException ex)
        {
            payment.MarkFailed($"{ex.Kind}: {ex.Message}");
            await _paymentRepository.UpdateAsync(payment, ct);
            throw;
        }
    }

    public async Task<PaymentView> FulfilAsync(int orderId, CancellationToken ct)
    {
        var payment = await _paymentRepository.GetByIdAsync(orderId, ct)
            ?? throw new PaymentFlowException(PaymentFlowError.NotFound, $"No payment found for order {orderId}.");

        // Repeated fulfil is a no-op: money already captured.
        if (payment.Status is PaymentStatus.Fulfilled or PaymentStatus.PartiallyRefunded or PaymentStatus.Refunded
            && payment.CaptureId is not null)
            return BuildView(payment);

        if (payment.Status != PaymentStatus.Authorized || payment.AuthorizationId is null)
            throw new PaymentFlowException(PaymentFlowError.Conflict,
                $"Order {orderId} is not awaiting fulfilment (status: {payment.Status}).");

        var authorizationId = payment.AuthorizationId;

        // Renew a stale hold rather than failing the fulfilment outright.
        if (await IsAuthorizationStaleAsync(authorizationId, payment.AuthorizationExpiresAt, ct))
        {
            _logger.LogInformation("Authorization {AuthId} for order {OrderId} is stale; reauthorizing.", authorizationId, orderId);
            var reauth = await _gateway.ReauthorizeAsync(authorizationId, ct); // throws AuthorizationNotRenewable if it cannot
            payment.RenewAuthorization(reauth.AuthorizationId, reauth.ExpiresAt);
            await _paymentRepository.UpdateAsync(payment, ct);
            authorizationId = reauth.AuthorizationId;
        }

        try
        {
            var capture = await _gateway.CaptureAsync(authorizationId, payment.Currency, ct);
            ApplyCapture(payment, capture.CaptureId, capture.Status, capture.GrossAmount, capture.PayPalFee, capture.NetAmount);
            await _paymentRepository.UpdateAsync(payment, ct);
            _logger.LogInformation("Order {OrderId} fulfilled: capture={CaptureId} gross={Gross} fee={Fee} net={Net}.",
                orderId, capture.CaptureId, capture.GrossAmount, capture.PayPalFee, capture.NetAmount);
            return BuildView(payment);
        }
        catch (PayPalGatewayException ex) when (ex.Kind == PaymentGatewayErrorKind.Unknown && payment.PayPalOrderId is not null)
        {
            // Transport failed after the capture may have landed — settle it by re-reading the order.
            _logger.LogWarning("Capture outcome unknown for order {OrderId}; re-reading PayPal order {PayPalOrder}.",
                orderId, payment.PayPalOrderId);
            var found = await _gateway.FindCaptureByPayPalOrderAsync(payment.PayPalOrderId, ct);
            if (found is not null)
            {
                ApplyCapture(payment, found.CaptureId, found.Status, found.GrossAmount, found.PayPalFee, found.NetAmount);
                await _paymentRepository.UpdateAsync(payment, ct);
                return BuildView(payment);
            }
            throw;
        }
    }

    public async Task<PaymentView> CancelAsync(int orderId, CancellationToken ct)
    {
        var payment = await _paymentRepository.GetByIdAsync(orderId, ct)
            ?? throw new PaymentFlowException(PaymentFlowError.NotFound, $"No payment found for order {orderId}.");

        if (payment.Status == PaymentStatus.Cancelled)
            return BuildView(payment); // already voided — no second void

        if (payment.Status != PaymentStatus.Authorized || payment.AuthorizationId is null)
            throw new PaymentFlowException(PaymentFlowError.Conflict,
                $"Order {orderId} cannot be cancelled (status: {payment.Status}). Only an authorized, unfulfilled order can be cancelled.");

        await _gateway.VoidAsync(payment.AuthorizationId, ct);
        payment.MarkCancelled();
        await _paymentRepository.UpdateAsync(payment, ct);
        _logger.LogInformation("Order {OrderId} cancelled; authorization {AuthId} voided.", orderId, payment.AuthorizationId);
        return BuildView(payment);
    }

    public async Task<Guid> RefundAsync(string buyerId, int orderId, decimal? amount, string idempotencyKey, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(idempotencyKey))
            throw new PaymentFlowException(PaymentFlowError.Validation, "An idempotency key is required for refunds.");

        var payment = await _paymentRepository.GetByIdAsync(orderId, ct)
            ?? throw new PaymentFlowException(PaymentFlowError.NotFound, $"No payment found for order {orderId}.");
        if (payment.BuyerId != buyerId)
            throw new PaymentFlowException(PaymentFlowError.NotFound, $"Order {orderId} was not found.");
        if (payment.CaptureId is null || payment.Status is not (PaymentStatus.Fulfilled or PaymentStatus.PartiallyRefunded))
            throw new PaymentFlowException(PaymentFlowError.Conflict,
                $"Order {orderId} has no captured payment to refund (status: {payment.Status}).");

        var refundAmount = amount ?? payment.RemainingRefundable();
        if (refundAmount <= 0m)
            throw new PaymentFlowException(PaymentFlowError.Validation, "The refund amount must be greater than zero.");
        if (!payment.CanRefund(refundAmount))
            throw new PaymentFlowException(PaymentFlowError.Validation,
                $"Refund of {refundAmount} exceeds the {payment.RemainingRefundable()} still refundable for order {orderId}.");

        // Idempotent-return for a repeated key.
        var priorByKey = await _refundRepository.FirstOrDefaultAsync(
            new OrderRefundByOrderAndKeySpecification(orderId, idempotencyKey), ct);
        if (priorByKey is not null)
        {
            _logger.LogInformation("Refund key {Key} for order {OrderId} already used; returning existing refund {RefundId}.",
                idempotencyKey, orderId, priorByKey.Id);
            return priorByKey.Id;
        }

        // Write the refund claim BEFORE calling PayPal; the (OrderId, IdempotencyKey) alternate key rejects a duplicate.
        var refund = new OrderRefund(orderId, idempotencyKey, refundAmount, payment.Currency);
        try
        {
            await _refundRepository.AddAsync(refund, ct);
        }
        catch (Exception ex) when (IsUniqueViolation(ex))
        {
            var winner = await _refundRepository.FirstOrDefaultAsync(
                new OrderRefundByOrderAndKeySpecification(orderId, idempotencyKey), ct);
            if (winner is not null) return winner.Id;
            throw new PaymentFlowException(PaymentFlowError.Conflict, "A refund under this key is already in progress.");
        }

        try
        {
            var result = await _gateway.RefundAsync(payment.CaptureId, refundAmount, payment.Currency, idempotencyKey, ct);
            refund.MarkCompleted(result.RefundId, result.Status);
            await _refundRepository.UpdateAsync(refund, ct);

            payment.RegisterRefund(refundAmount);
            await _paymentRepository.UpdateAsync(payment, ct);
            _logger.LogInformation("Order {OrderId} refunded {Amount} (refund {RefundId}, status {Status}).",
                orderId, refundAmount, result.RefundId, result.Status);
            return refund.Id;
        }
        catch (PayPalGatewayException ex) when (ex.Kind != PaymentGatewayErrorKind.Unknown)
        {
            // Definite rejection — release the claim so a corrected retry is possible.
            await _refundRepository.DeleteAsync(refund, ct);
            throw;
        }
        // Unknown outcome: keep the Pending claim so a same-key retry does not double-refund; reconciliation surfaces it.
    }

    public async Task<IReadOnlyList<MyOrderView>> GetMyOrdersAsync(string buyerId, CancellationToken ct)
    {
        var orders = await _orderRepository.ListAsync(new CustomerOrdersWithItemsSpecification(buyerId), ct);
        var views = new List<MyOrderView>();
        foreach (var order in orders)
        {
            var payment = await _paymentRepository.GetByIdAsync(order.Id, ct);
            var items = order.OrderItems
                .Select(i => new MyOrderItemView(i.ItemOrdered.CatalogItemId, i.ItemOrdered.ProductName, i.UnitPrice, i.Units))
                .ToList();
            var status = payment is null ? nameof(PaymentStatus.PendingPayment) : payment.Status.ToString();
            views.Add(new MyOrderView(order.Id, order.OrderDate, order.Total(), _config.Currency, status,
                payment is null ? null : BuildView(payment), items));
        }
        return views;
    }

    // ---- helpers ----

    private async Task<bool> IsAuthorizationStaleAsync(string authorizationId, string? storedExpiry, CancellationToken ct)
    {
        if (TryExpired(storedExpiry)) return true;
        var status = await _gateway.GetAuthorizationAsync(authorizationId, ct);
        if (!string.Equals(status.Status, "CREATED", StringComparison.OrdinalIgnoreCase)
            && !string.Equals(status.Status, "PENDING", StringComparison.OrdinalIgnoreCase))
        {
            // Anything other than a fresh/pending hold (e.g. EXPIRED) needs renewal before capture.
            return true;
        }
        return TryExpired(status.ExpiresAt);
    }

    private static bool TryExpired(string? iso)
    {
        if (string.IsNullOrWhiteSpace(iso)) return false;
        return DateTimeOffset.TryParse(iso, CultureInfo.InvariantCulture,
            DateTimeStyles.AdjustToUniversal, out var when) && when <= DateTimeOffset.UtcNow;
    }

    private static void ApplyCapture(OrderPayment payment, string captureId, string status,
        decimal gross, decimal? fee, decimal? net)
    {
        if (!string.Equals(status, "COMPLETED", StringComparison.OrdinalIgnoreCase))
        {
            // PENDING/DECLINED/FAILED — do not claim fulfilment on an outcome that is not settled success.
            throw new PayPalGatewayException(
                $"PayPal capture returned status '{status}', which is not a completed capture.",
                status is "PENDING" ? PaymentGatewayErrorKind.Unknown : PaymentGatewayErrorKind.Validation,
                issue: status);
        }
        payment.MarkFulfilled(captureId, gross, fee, net);
    }

    private static PaymentView BuildView(OrderPayment p) => new(
        p.OrderId, p.Status.ToString(), p.Currency, p.AuthorizedAmount, p.CapturedAmount, p.PayPalFee,
        p.NetAmount, p.RefundedAmount, p.PayPalOrderId, p.AuthorizationId, p.CaptureId, p.LastError);

    private static Address BuildAddress(ShippingAddressInput? shipTo) => new(
        Blank(shipTo?.Street),
        Blank(shipTo?.City),
        shipTo?.State ?? string.Empty,
        Blank(shipTo?.Country),
        Blank(shipTo?.ZipCode));

    private static string Blank(string? value) => string.IsNullOrWhiteSpace(value) ? "N/A" : value!.Trim();

    /// <summary>True when the exception is a store unique/primary/alternate-key violation (both SqlServer and InMemory).</summary>
    private static bool IsUniqueViolation(Exception ex)
    {
        for (Exception? e = ex; e is not null; e = e.InnerException)
        {
            if (e is DbUpdateException) return true;
            var msg = e.Message ?? string.Empty;
            if (msg.Contains("same key", StringComparison.OrdinalIgnoreCase)      // InMemory duplicate key
                || msg.Contains("duplicate", StringComparison.OrdinalIgnoreCase)
                || msg.Contains("unique", StringComparison.OrdinalIgnoreCase))
                return true;
        }
        return false;
    }
}
