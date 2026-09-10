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
using Microsoft.eShopWeb.ApplicationCore.Payments;
using Microsoft.eShopWeb.ApplicationCore.Specifications;

namespace Microsoft.eShopWeb.ApplicationCore.Services;

public class OrderPaymentService : IOrderPaymentService
{
    private readonly IRepository<Order> _orderRepository;
    private readonly IRepository<OrderPayment> _paymentRepository;
    private readonly IReadRepository<CatalogItem> _itemRepository;
    private readonly IRepository<SavedCard> _savedCardRepository;
    private readonly IPayPalGateway _gateway;
    private readonly IUriComposer _uriComposer;
    private readonly IAppLogger<OrderPaymentService> _logger;
    private readonly string _currency;

    public OrderPaymentService(
        IRepository<Order> orderRepository,
        IRepository<OrderPayment> paymentRepository,
        IReadRepository<CatalogItem> itemRepository,
        IRepository<SavedCard> savedCardRepository,
        IPayPalGateway gateway,
        IUriComposer uriComposer,
        IAppLogger<OrderPaymentService> logger,
        IPayPalCurrencyProvider currencyProvider)
    {
        _orderRepository = orderRepository;
        _paymentRepository = paymentRepository;
        _itemRepository = itemRepository;
        _savedCardRepository = savedCardRepository;
        _gateway = gateway;
        _uriComposer = uriComposer;
        _logger = logger;
        _currency = currencyProvider.Currency;
    }

    public async Task<PlaceOrderResult> PlaceOrderAsync(string buyerId, IReadOnlyList<OrderLineInput> items,
        Address shipToAddress, CancellationToken ct)
    {
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));
        if (items is null || items.Count == 0)
            throw new PaymentValidationException("An order must contain at least one item.");
        if (items.Any(i => i.Quantity <= 0))
            throw new PaymentValidationException("Every item quantity must be greater than zero.");

        var ids = items.Select(i => i.CatalogItemId).Distinct().ToArray();
        var catalogItems = await _itemRepository.ListAsync(new CatalogItemsSpecification(ids), ct);
        var missing = ids.Where(id => catalogItems.All(c => c.Id != id)).ToArray();
        if (missing.Length > 0)
            throw new PaymentValidationException($"Unknown catalog item id(s): {string.Join(", ", missing)}.");

        var orderItems = items.Select(line =>
        {
            var catalogItem = catalogItems.First(c => c.Id == line.CatalogItemId);
            var itemOrdered = new CatalogItemOrdered(catalogItem.Id, catalogItem.Name,
                _uriComposer.ComposePicUri(catalogItem.PictureUri));
            return new OrderItem(itemOrdered, catalogItem.Price, line.Quantity);
        }).ToList();

        var order = new Order(buyerId, shipToAddress, orderItems);
        order = await _orderRepository.AddAsync(order, ct);

        var payment = new OrderPayment(order.Id, buyerId, order.Total(), _currency);
        await _paymentRepository.AddAsync(payment, ct);

        _logger.LogInformation("Placed order {0} for buyer, total {1} {2}", order.Id, payment.Amount, _currency);
        return new PlaceOrderResult(order.Id, payment.Amount, _currency, payment.Status);
    }

    public async Task<OrderPaymentView> PayAsync(string buyerId, int orderId, CardDetails? card, int? savedCardId,
        CancellationToken ct)
    {
        var payment = await LoadOwnedPaymentAsync(buyerId, orderId, ct);

        // Idempotent: a double-click on an already-held or already-captured order returns current state.
        if (payment.Status is PaymentStatus.Authorized or PaymentStatus.Captured
            or PaymentStatus.PartiallyRefunded or PaymentStatus.Refunded)
        {
            return await ToViewAsync(payment, ct);
        }
        if (payment.Status is not (PaymentStatus.PendingPayment or PaymentStatus.Failed))
            throw new PaymentValidationException($"Order {orderId} cannot be paid from state {payment.Status}.");

        string? vaultId = null;
        if (savedCardId.HasValue)
        {
            var saved = await _savedCardRepository.GetByIdAsync(savedCardId.Value, ct);
            if (saved is null || saved.BuyerId != buyerId)
                throw new PaymentNotFoundException($"Saved card {savedCardId} was not found.");
            vaultId = saved.PayPalVaultId;
        }
        else if (card is null)
        {
            throw new PaymentValidationException("Provide either card details or a saved paymentMethodId.");
        }

        // Persist the idempotency key before the call so a retry dedups at PayPal.
        var key = payment.EnsureAuthorizeRequestId();
        await _paymentRepository.UpdateAsync(payment, ct);

        try
        {
            var outcome = await _gateway.AuthorizeAsync(
                invoiceId: payment.InvoiceId,
                amount: payment.Amount,
                currency: payment.Currency,
                description: $"eShop order {orderId}",
                card: card,
                vaultId: vaultId,
                idempotencyKey: key,
                ct: ct);

            payment.MarkAuthorized(outcome.PayPalOrderId, outcome.AuthorizationId, outcome.ExpiresAt);
            await _paymentRepository.UpdateAsync(payment, ct);
            _logger.LogInformation("Authorized order {0}: paypalOrder {1}, auth {2}", orderId,
                outcome.PayPalOrderId, outcome.AuthorizationId);
            return await ToViewAsync(payment, ct);
        }
        catch (PaymentChallengeException)
        {
            throw;
        }
        catch (PaymentGatewayException ex)
        {
            payment.MarkFailed(ex.Message);
            await _paymentRepository.UpdateAsync(payment, ct);
            throw;
        }
    }

    public async Task<OrderPaymentView> FulfilAsync(int orderId, CancellationToken ct)
    {
        var payment = await LoadPaymentAsync(orderId, ct);

        if (payment.Status is PaymentStatus.Captured or PaymentStatus.PartiallyRefunded or PaymentStatus.Refunded)
            return await ToViewAsync(payment, ct); // already fulfilled — idempotent

        if (payment.Status != PaymentStatus.Authorized || payment.AuthorizationId is null)
            throw new PaymentValidationException(
                $"Order {orderId} cannot be fulfilled from state {payment.Status}; it must be authorized first.");

        // Renew a stale hold before capturing, rather than failing the fulfilment outright.
        if (IsAuthorizationStale(payment.AuthorizationExpiresAt))
            await RenewAuthorizationAsync(payment, orderId, ct);

        var captureKey = payment.EnsureCaptureRequestId();
        await _paymentRepository.UpdateAsync(payment, ct);

        CaptureOutcome capture;
        try
        {
            capture = await _gateway.CaptureAsync(payment.AuthorizationId!, payment.Amount, payment.Currency,
                payment.InvoiceId, captureKey, ct);
        }
        catch (PaymentGatewayException ex) when (IsExpiredAuthorization(ex))
        {
            // The hold went stale between our check and the capture: renew once, then capture again.
            _logger.LogWarning("Capture of order {0} hit an expired authorization ({1}); re-authorizing.",
                orderId, ex.Issue ?? "expired");
            await RenewAuthorizationAsync(payment, orderId, ct);
            await _paymentRepository.UpdateAsync(payment, ct);
            capture = await _gateway.CaptureAsync(payment.AuthorizationId!, payment.Amount, payment.Currency,
                payment.InvoiceId, captureKey, ct);
        }

        payment.MarkCaptured(capture.CaptureId, capture.Amount, capture.Fee, capture.Net);
        await _paymentRepository.UpdateAsync(payment, ct);
        _logger.LogInformation("Fulfilled order {0}: captured {1} {2}, fee {3}, net {4}, capture {5}", orderId,
            capture.Amount, payment.Currency, capture.Fee, capture.Net, capture.CaptureId);
        return await ToViewAsync(payment, ct);
    }

    public async Task<OrderPaymentView> CancelAsync(int orderId, CancellationToken ct)
    {
        var payment = await LoadPaymentAsync(orderId, ct);

        if (payment.Status == PaymentStatus.Cancelled)
            return await ToViewAsync(payment, ct); // idempotent

        if (payment.Status != PaymentStatus.Authorized || payment.AuthorizationId is null)
            throw new PaymentValidationException(
                $"Order {orderId} cannot be cancelled from state {payment.Status}; only an authorized, un-captured order can be cancelled.");

        var key = payment.EnsureVoidRequestId();
        await _paymentRepository.UpdateAsync(payment, ct);

        await _gateway.VoidAsync(payment.AuthorizationId!, key, ct);
        payment.MarkCancelled();
        await _paymentRepository.UpdateAsync(payment, ct);
        _logger.LogInformation("Cancelled order {0}: released authorization {1}", orderId, payment.AuthorizationId);
        return await ToViewAsync(payment, ct);
    }

    public async Task<RefundResult> RefundAsync(string buyerId, int orderId, decimal? amount, string idempotencyKey,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(idempotencyKey))
            throw new PaymentValidationException("A refund idempotency key is required.");

        var payment = await LoadOwnedPaymentAsync(buyerId, orderId, ct);

        if (payment.Status is not (PaymentStatus.Captured or PaymentStatus.PartiallyRefunded))
            throw new PaymentValidationException(
                $"Order {orderId} cannot be refunded from state {payment.Status}; it must be captured first.");

        // Idempotent on the caller-supplied key: a repeat returns the same refund.
        var existing = payment.FindRefundByKey(idempotencyKey);
        if (existing is not null)
            return new RefundResult(existing.PayPalRefundId, existing.Amount, existing.Status);

        var remaining = payment.RefundableRemaining();
        var refundAmount = amount ?? remaining;
        if (refundAmount <= 0m)
            throw new PaymentValidationException("Refund amount must be greater than zero.");
        if (refundAmount > remaining)
            throw new PaymentValidationException(
                $"Refund of {refundAmount} {payment.Currency} exceeds the refundable remaining {remaining} {payment.Currency}.");

        var outcome = await _gateway.RefundAsync(payment.CaptureId!, amount, payment.Currency,
            payment.InvoiceId, idempotencyKey, ct);

        payment.AddRefund(new PaymentRefund(idempotencyKey, outcome.RefundId, outcome.Amount, outcome.Status));
        await _paymentRepository.UpdateAsync(payment, ct);
        _logger.LogInformation("Refunded {0} {1} on order {2}: refund {3} (status {4})", outcome.Amount,
            payment.Currency, orderId, outcome.RefundId, outcome.Status);
        return new RefundResult(outcome.RefundId, outcome.Amount, outcome.Status);
    }

    public async Task<IReadOnlyList<OrderPaymentView>> GetMyOrdersAsync(string buyerId, CancellationToken ct)
    {
        var payments = await _paymentRepository.ListAsync(new OrderPaymentsByBuyerSpec(buyerId), ct);
        var orders = await _orderRepository.ListAsync(new CustomerOrdersWithItemsSpecification(buyerId), ct);
        var ordersById = orders.ToDictionary(o => o.Id);

        return payments
            .OrderByDescending(p => p.OrderId)
            .Select(p => ToView(p, ordersById.GetValueOrDefault(p.OrderId)))
            .ToList();
    }

    // --- helpers ---

    private async Task RenewAuthorizationAsync(OrderPayment payment, int orderId, CancellationToken ct)
    {
        try
        {
            var key = $"reauth-{orderId}-{Guid.NewGuid():N}";
            var reauth = await _gateway.ReauthorizeAsync(payment.AuthorizationId!, payment.Amount,
                payment.Currency, key, ct);
            payment.RenewAuthorization(reauth.AuthorizationId, reauth.ExpiresAt);
            _logger.LogInformation("Re-authorized order {0}: new auth {1}", orderId, reauth.AuthorizationId);
        }
        catch (PaymentGatewayException ex)
        {
            var message =
                $"The authorization for order {orderId} has expired and can no longer be renewed " +
                $"({ex.Issue ?? ex.Message}). Collect a new payment from the shopper before fulfilling.";
            payment.SetOperatorError(message);
            await _paymentRepository.UpdateAsync(payment, ct);
            throw new PaymentGatewayException(message, ex.Issue, ex.DebugId, ex.StatusCode, ex);
        }
    }

    private static bool IsAuthorizationStale(string? expiresAt)
    {
        if (string.IsNullOrWhiteSpace(expiresAt)) return false;
        return DateTimeOffset.TryParse(expiresAt, CultureInfo.InvariantCulture,
                   DateTimeStyles.RoundtripKind, out var when)
               && when <= DateTimeOffset.UtcNow;
    }

    private static bool IsExpiredAuthorization(PaymentGatewayException ex)
    {
        var issue = ex.Issue;
        return issue is not null &&
               (issue.Contains("EXPIRED", StringComparison.OrdinalIgnoreCase)
                || issue.Contains("AUTHORIZATION", StringComparison.OrdinalIgnoreCase)
                   && issue.Contains("VOID", StringComparison.OrdinalIgnoreCase));
    }


    private async Task<OrderPayment> LoadPaymentAsync(int orderId, CancellationToken ct)
    {
        var payment = await _paymentRepository.FirstOrDefaultAsync(new OrderPaymentByOrderIdSpec(orderId), ct);
        if (payment is null)
            throw new PaymentNotFoundException($"Order {orderId} was not found.");
        return payment;
    }

    private async Task<OrderPayment> LoadOwnedPaymentAsync(string buyerId, int orderId, CancellationToken ct)
    {
        var payment = await _paymentRepository.FirstOrDefaultAsync(new OrderPaymentByOrderIdSpec(orderId), ct);
        if (payment is null || payment.BuyerId != buyerId)
            throw new PaymentNotFoundException($"Order {orderId} was not found."); // don't reveal others' orders
        return payment;
    }

    private async Task<OrderPaymentView> ToViewAsync(OrderPayment payment, CancellationToken ct)
    {
        var order = await _orderRepository.FirstOrDefaultAsync(new OrderWithItemsByIdSpec(payment.OrderId), ct);
        return ToView(payment, order);
    }

    private static OrderPaymentView ToView(OrderPayment p, Order? order)
    {
        var items = order?.OrderItems
            .Select(i => new OrderPaymentLineView(i.ItemOrdered.CatalogItemId, i.ItemOrdered.ProductName,
                i.UnitPrice, i.Units))
            .ToList() ?? new List<OrderPaymentLineView>();

        var refunds = p.Refunds
            .Select(r => new RefundView(r.PayPalRefundId, r.Amount, r.Status, r.CreatedAt))
            .ToList();

        return new OrderPaymentView(
            p.OrderId, order?.OrderDate ?? default, p.BuyerId, p.Amount, p.Currency, p.Status,
            p.PayPalOrderId, p.AuthorizationId, p.AuthorizationExpiresAt, p.CaptureId, p.CapturedAmount,
            p.PayPalFee, p.NetAmount, p.RefundedAmount, p.LastError, items, refunds);
    }
}
