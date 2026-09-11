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

/// <summary>Default <see cref="IOrderPaymentService"/> orchestrating the order + PayPal payment lifecycle.</summary>
public class OrderPaymentService : IOrderPaymentService
{
    private readonly IRepository<Order> _orderRepository;
    private readonly IRepository<OrderPayment> _paymentRepository;
    private readonly IRepository<CatalogItem> _itemRepository;
    private readonly IRepository<SavedPaymentMethod> _savedCardRepository;
    private readonly IPayPalClient _payPal;
    private readonly IUriComposer _uriComposer;
    private readonly PayPalSettings _settings;
    private readonly IAppLogger<OrderPaymentService> _logger;

    public OrderPaymentService(
        IRepository<Order> orderRepository,
        IRepository<OrderPayment> paymentRepository,
        IRepository<CatalogItem> itemRepository,
        IRepository<SavedPaymentMethod> savedCardRepository,
        IPayPalClient payPal,
        IUriComposer uriComposer,
        PayPalSettings settings,
        IAppLogger<OrderPaymentService> logger)
    {
        _orderRepository = orderRepository;
        _paymentRepository = paymentRepository;
        _itemRepository = itemRepository;
        _savedCardRepository = savedCardRepository;
        _payPal = payPal;
        _uriComposer = uriComposer;
        _settings = settings;
        _logger = logger;
    }

    private string Currency => _settings.Currency;

    // A per-process token keeps invoice ids unique across app restarts (the in-memory store resets order
    // ids to 1 each run, but the merchant account requires globally-unique invoice ids). Reconciliation
    // runs in the same process, so it reproduces the same reference for this run's orders.
    private static readonly string RunToken = Guid.NewGuid().ToString("N").Substring(0, 8);

    /// <summary>External reference stamped on PayPal orders (invoice_id + custom_id) for reconciliation.</summary>
    public static string InvoiceIdFor(int orderId) => $"ESHOP-{orderId}-{RunToken}";

    // Idempotency keys must be stable within a run (so a retry replays the original result) but distinct
    // across runs (so a fresh run is not served a stale/failed cached result for a reused order id).
    private static string Req(string suffix) => $"eshop-{suffix}-{RunToken}";

    public async Task<Order> PlaceOrderAsync(string buyerId, IReadOnlyCollection<OrderLineInput> lines, ShippingAddressInput? shippingAddress, CancellationToken cancellationToken = default)
    {
        if (lines is null || lines.Count == 0)
        {
            throw new PaymentValidationException("An order must contain at least one line item.");
        }

        // Collapse duplicate lines and reject non-positive quantities.
        var requested = new Dictionary<int, int>();
        foreach (var line in lines)
        {
            if (line.Quantity <= 0)
            {
                throw new PaymentValidationException($"Quantity for catalog item {line.CatalogItemId} must be greater than zero.");
            }
            requested[line.CatalogItemId] = requested.TryGetValue(line.CatalogItemId, out var q) ? q + line.Quantity : line.Quantity;
        }

        var catalogItems = await _itemRepository.ListAsync(new CatalogItemsSpecification(requested.Keys.ToArray()), cancellationToken);
        var missing = requested.Keys.Where(id => catalogItems.All(c => c.Id != id)).ToArray();
        if (missing.Length > 0)
        {
            throw new PaymentValidationException($"Unknown catalog item id(s): {string.Join(", ", missing)}.");
        }

        var orderItems = requested.Select(kvp =>
        {
            var catalogItem = catalogItems.First(c => c.Id == kvp.Key);
            var itemOrdered = new CatalogItemOrdered(catalogItem.Id, catalogItem.Name, _uriComposer.ComposePicUri(catalogItem.PictureUri));
            return new OrderItem(itemOrdered, catalogItem.Price, kvp.Value);
        }).ToList();

        var address = ToAddress(shippingAddress);
        var order = new Order(buyerId, address, orderItems);
        order = await _orderRepository.AddAsync(order, cancellationToken);

        var payment = new OrderPayment(order.Id, buyerId, order.Total(), Currency);
        await _paymentRepository.AddAsync(payment, cancellationToken);

        _logger.LogInformation($"Placed order {order.Id} for {buyerId} awaiting payment; total {payment.Amount} {Currency}.");
        return order;
    }

    public async Task<OrderPayment> PayAsync(string buyerId, int orderId, PayInstruction instruction, CancellationToken cancellationToken = default)
    {
        var order = await LoadOwnedOrderAsync(buyerId, orderId, cancellationToken);
        var payment = await LoadPaymentAsync(order.Id, cancellationToken);

        switch (payment.Status)
        {
            case PaymentStatus.Authorized:
                // Double-click / retry: the hold already exists. Return it unchanged.
                return payment;
            case PaymentStatus.Captured:
            case PaymentStatus.PartiallyRefunded:
            case PaymentStatus.Refunded:
                throw new PaymentStateConflictException($"Order {orderId} has already been paid and captured.");
            case PaymentStatus.Cancelled:
                throw new PaymentStateConflictException($"Order {orderId} was cancelled and can no longer be paid.");
        }

        var source = await ResolvePaymentSourceAsync(buyerId, instruction, cancellationToken);

        var result = await _payPal.AuthorizeAsync(
            payment.Amount, payment.CurrencyCode, InvoiceIdFor(orderId), source,
            requestId: Req($"pay-{orderId}"), cancellationToken);

        payment.MarkAuthorized(result.PayPalOrderId, result.AuthorizationId, result.Status, result.ExpiresAt);
        await _paymentRepository.UpdateAsync(payment, cancellationToken);

        _logger.LogInformation($"Authorized order {orderId}: authorization {result.AuthorizationId} status {result.Status}.");
        return payment;
    }

    public async Task<OrderPayment> FulfilAsync(int orderId, CancellationToken cancellationToken = default)
    {
        var payment = await LoadPaymentAsync(orderId, cancellationToken);

        if (payment.Status is PaymentStatus.Captured or PaymentStatus.PartiallyRefunded or PaymentStatus.Refunded)
        {
            return payment; // Already fulfilled; idempotent.
        }
        if (payment.Status != PaymentStatus.Authorized || string.IsNullOrEmpty(payment.AuthorizationId))
        {
            throw new PaymentStateConflictException($"Order {orderId} is not awaiting fulfilment (status: {payment.Status}).");
        }

        var authorizationId = payment.AuthorizationId!;

        // Renew a hold we already know is stale before attempting capture.
        if (IsAuthorizationStale(payment))
        {
            authorizationId = await RenewAuthorizationAsync(payment, cancellationToken);
        }

        CaptureResult capture;
        try
        {
            capture = await _payPal.CaptureAsync(authorizationId, amount: null, payment.CurrencyCode,
                requestId: Req($"capture-{orderId}"), finalCapture: true, cancellationToken);
        }
        catch (PayPalApiException ex) when (LooksLikeExpiredAuthorization(ex))
        {
            // The hold went stale between our check and the capture; renew and capture the fresh hold.
            authorizationId = await RenewAuthorizationAsync(payment, cancellationToken);
            capture = await _payPal.CaptureAsync(authorizationId, amount: null, payment.CurrencyCode,
                requestId: Req($"capture-{orderId}-renewed"), finalCapture: true, cancellationToken);
        }

        payment.MarkCaptured(capture.CaptureId, capture.Status, capture.GrossAmount, capture.PayPalFee, capture.NetAmount);
        await _paymentRepository.UpdateAsync(payment, cancellationToken);

        _logger.LogInformation($"Fulfilled order {orderId}: capture {capture.CaptureId} gross {capture.GrossAmount} fee {capture.PayPalFee} net {capture.NetAmount} {capture.CurrencyCode}.");
        return payment;
    }

    public async Task<OrderPayment> CancelAsync(int orderId, CancellationToken cancellationToken = default)
    {
        var payment = await LoadPaymentAsync(orderId, cancellationToken);

        if (payment.Status == PaymentStatus.Cancelled)
        {
            return payment; // Idempotent.
        }
        if (payment.Status is PaymentStatus.Captured or PaymentStatus.PartiallyRefunded or PaymentStatus.Refunded)
        {
            throw new PaymentStateConflictException($"Order {orderId} has already been fulfilled; issue a refund instead of a cancellation.");
        }
        if (payment.Status != PaymentStatus.Authorized || string.IsNullOrEmpty(payment.AuthorizationId))
        {
            throw new PaymentStateConflictException($"Order {orderId} has no active hold to release (status: {payment.Status}).");
        }

        await _payPal.VoidAsync(payment.AuthorizationId!, requestId: Req($"cancel-{orderId}"), cancellationToken);
        payment.MarkCancelled();
        await _paymentRepository.UpdateAsync(payment, cancellationToken);

        _logger.LogInformation($"Cancelled order {orderId}; hold {payment.AuthorizationId} released.");
        return payment;
    }

    public async Task<OrderPayment> RefundAsync(string buyerId, int orderId, decimal? amount, string idempotencyKey, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(idempotencyKey))
        {
            throw new PaymentValidationException("An idempotency key is required for refunds.");
        }

        var order = await LoadOwnedOrderAsync(buyerId, orderId, cancellationToken);
        var payment = await LoadPaymentAsync(order.Id, cancellationToken);

        // Repeat under the same key returns the original refund without refunding again.
        if (payment.FindRefundByKey(idempotencyKey) is not null)
        {
            return payment;
        }

        if (payment.Status is not (PaymentStatus.Captured or PaymentStatus.PartiallyRefunded) || string.IsNullOrEmpty(payment.CaptureId))
        {
            throw new PaymentStateConflictException($"Order {orderId} has no captured payment to refund (status: {payment.Status}).");
        }

        var remaining = payment.RefundableRemaining();
        var refundAmount = amount ?? remaining;

        if (refundAmount <= 0m)
        {
            throw new PaymentValidationException("Refund amount must be greater than zero.");
        }
        if (refundAmount > remaining)
        {
            throw new PaymentValidationException(
                $"Refund of {Money.Format(refundAmount, _settings.CurrencyDecimals)} exceeds the refundable remaining of {Money.Format(remaining, _settings.CurrencyDecimals)} {payment.CurrencyCode}.");
        }

        var result = await _payPal.RefundAsync(payment.CaptureId!, refundAmount, payment.CurrencyCode,
            requestId: Req($"refund-{orderId}-{idempotencyKey}"), cancellationToken);

        payment.AddRefund(result.RefundId, refundAmount, result.Status, idempotencyKey);
        await _paymentRepository.UpdateAsync(payment, cancellationToken);

        _logger.LogInformation($"Refunded order {orderId}: refund {result.RefundId} amount {refundAmount} status {result.Status}; total refunded {payment.TotalRefunded()}.");
        return payment;
    }

    public async Task<IReadOnlyList<OrderWithPayment>> GetMyOrdersAsync(string buyerId, CancellationToken cancellationToken = default)
    {
        var orders = await _orderRepository.ListAsync(new CustomerOrdersWithItemsSpecification(buyerId), cancellationToken);
        var payments = await _paymentRepository.ListAsync(new OrderPaymentsByBuyerSpec(buyerId), cancellationToken);
        var byOrderId = payments.ToDictionary(p => p.OrderId);

        return orders
            .Where(o => byOrderId.ContainsKey(o.Id))
            .OrderByDescending(o => o.Id)
            .Select(o => new OrderWithPayment(o, byOrderId[o.Id]))
            .ToList();
    }

    // --- helpers -------------------------------------------------------------------------------

    private async Task<Order> LoadOwnedOrderAsync(string buyerId, int orderId, CancellationToken cancellationToken)
    {
        var order = await _orderRepository.FirstOrDefaultAsync(new OrderWithItemsByIdSpec(orderId), cancellationToken);
        if (order is null || order.BuyerId != buyerId)
        {
            // Do not leak the existence of other shoppers' orders.
            throw new PaymentResourceNotFoundException($"Order {orderId} was not found.");
        }
        return order;
    }

    private async Task<OrderPayment> LoadPaymentAsync(int orderId, CancellationToken cancellationToken)
    {
        var payment = await _paymentRepository.FirstOrDefaultAsync(new OrderPaymentByOrderIdSpec(orderId), cancellationToken);
        if (payment is null)
        {
            throw new PaymentResourceNotFoundException($"Order {orderId} was not found.");
        }
        return payment;
    }

    private async Task<PaymentSource> ResolvePaymentSourceAsync(string buyerId, PayInstruction instruction, CancellationToken cancellationToken)
    {
        if (instruction.SavedPaymentMethodId is int savedId)
        {
            var saved = await _savedCardRepository.FirstOrDefaultAsync(new SavedPaymentMethodByIdSpec(savedId, buyerId), cancellationToken);
            if (saved is null)
            {
                throw new PaymentResourceNotFoundException($"Saved card {savedId} was not found.");
            }
            return PaymentSource.FromVault(saved.PaymentTokenId);
        }

        if (instruction.Card is not null)
        {
            return PaymentSource.FromCard(instruction.Card);
        }

        throw new PaymentValidationException("Provide either card details or a saved card id to pay with.");
    }

    private static bool IsAuthorizationStale(OrderPayment payment)
        => payment.AuthorizationExpiresAt.HasValue
           && payment.AuthorizationExpiresAt.Value <= DateTimeOffset.UtcNow.AddMinutes(1);

    private static bool LooksLikeExpiredAuthorization(PayPalApiException ex)
        => ex.StatusCode is 422 or 400
           && (ex.Message?.Contains("EXPIRED", StringComparison.OrdinalIgnoreCase) == true
               || ex.PayPalName?.Contains("EXPIRED", StringComparison.OrdinalIgnoreCase) == true);

    private async Task<string> RenewAuthorizationAsync(OrderPayment payment, CancellationToken cancellationToken)
    {
        try
        {
            var renewed = await _payPal.ReauthorizeAsync(payment.AuthorizationId!, payment.Amount, payment.CurrencyCode,
                requestId: Req($"reauth-{payment.OrderId}-{DateTimeOffset.UtcNow:yyyyMMddHHmmss}"), cancellationToken);
            payment.UpdateAuthorization(renewed.AuthorizationId, renewed.Status, renewed.ExpiresAt);
            await _paymentRepository.UpdateAsync(payment, cancellationToken);
            _logger.LogInformation($"Renewed stale authorization for order {payment.OrderId}: new authorization {renewed.AuthorizationId}.");
            return renewed.AuthorizationId;
        }
        catch (PayPalApiException ex)
        {
            throw new PaymentStateConflictException(
                $"The authorization for order {payment.OrderId} has expired and can no longer be renewed" +
                $" (PayPal: {ex.PayPalName ?? "error"}, debug_id {ex.DebugId ?? "n/a"}). " +
                $"Ask the shopper to pay again via POST /api/orders/{payment.OrderId}/pay to place a fresh hold, then fulfil.");
        }
    }

    private static Address ToAddress(ShippingAddressInput? input)
    {
        if (input is null)
        {
            // No storefront UI supplies an address; use a placeholder so the order aggregate stays valid.
            return new Address("N/A", "N/A", "N/A", "US", "00000");
        }
        return new Address(input.Street, input.City, input.State, input.Country, input.ZipCode);
    }
}
