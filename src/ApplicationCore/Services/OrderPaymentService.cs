using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Ardalis.GuardClauses;
using Microsoft.eShopWeb.ApplicationCore.Entities;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Specifications;

namespace Microsoft.eShopWeb.ApplicationCore.Services;

public class OrderPaymentService : IOrderPaymentService
{
    private readonly IRepository<Order> _orderRepository;
    private readonly IRepository<OrderPayment> _paymentRepository;
    private readonly IRepository<SavedPaymentMethod> _savedCardRepository;
    private readonly IReadRepository<CatalogItem> _itemRepository;
    private readonly IPayPalPaymentGateway _gateway;
    private readonly IUriComposer _uriComposer;
    private readonly IAppLogger<OrderPaymentService> _logger;

    // Default ship-to used when a caller omits an address (mirrors the storefront checkout default).
    private static readonly ShippingAddressInput DefaultShipping =
        new("123 Main St.", "Kent", "OH", "United States", "44240");

    public OrderPaymentService(
        IRepository<Order> orderRepository,
        IRepository<OrderPayment> paymentRepository,
        IRepository<SavedPaymentMethod> savedCardRepository,
        IReadRepository<CatalogItem> itemRepository,
        IPayPalPaymentGateway gateway,
        IUriComposer uriComposer,
        IAppLogger<OrderPaymentService> logger)
    {
        _orderRepository = orderRepository;
        _paymentRepository = paymentRepository;
        _savedCardRepository = savedCardRepository;
        _itemRepository = itemRepository;
        _gateway = gateway;
        _uriComposer = uriComposer;
        _logger = logger;
    }

    public async Task<int> PlaceOrderAsync(string buyerId, IReadOnlyList<OrderLine> lines, ShippingAddressInput? shipping, CancellationToken ct)
    {
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));
        if (lines is null || lines.Count == 0)
            throw new PaymentValidationException("An order must contain at least one item.");
        if (lines.Any(l => l.Quantity <= 0))
            throw new PaymentValidationException("Every item quantity must be greater than zero.");

        var ids = lines.Select(l => l.CatalogItemId).Distinct().ToArray();
        var catalogItems = await _itemRepository.ListAsync(new CatalogItemsSpecification(ids), ct);
        var byId = catalogItems.ToDictionary(c => c.Id);

        var orderItems = new List<OrderItem>();
        foreach (var line in lines)
        {
            if (!byId.TryGetValue(line.CatalogItemId, out var catalogItem))
                throw new PaymentValidationException($"Catalog item {line.CatalogItemId} does not exist.");

            // Price comes from the catalog server-side; the client's numbers are never trusted.
            var itemOrdered = new CatalogItemOrdered(catalogItem.Id, catalogItem.Name,
                _uriComposer.ComposePicUri(catalogItem.PictureUri));
            orderItems.Add(new OrderItem(itemOrdered, catalogItem.Price, line.Quantity));
        }

        var s = shipping ?? DefaultShipping;
        var address = new Address(s.Street, s.City, s.State, s.Country, s.ZipCode);
        var order = new Order(buyerId, address, orderItems);
        order = await _orderRepository.AddAsync(order, ct);

        var total = decimal.Round(order.Total(), 2, MidpointRounding.AwayFromZero);
        if (total <= 0m)
            throw new PaymentValidationException("Order total must be greater than zero.");

        var payment = new OrderPayment(order.Id, buyerId, total, _gateway.CurrencyCode);
        await _paymentRepository.AddAsync(payment, ct);

        _logger.LogInformation($"Order {order.Id} placed for {buyerId}; awaiting payment ({total} {_gateway.CurrencyCode}).");
        return order.Id;
    }

    public async Task<OrderPayment> PayAsync(string buyerId, int orderId, PayInstruction instruction, CancellationToken ct)
    {
        var payment = await LoadOwnedPaymentAsync(buyerId, orderId, ct);

        // Idempotent in effect: a double-click never authorizes twice.
        if (payment.Status == OrderPaymentStatus.Authorized)
            return payment;
        if (payment.Status is not (OrderPaymentStatus.AwaitingPayment or OrderPaymentStatus.Failed))
            throw new PaymentConflictException($"Order {orderId} cannot be paid in state {payment.Status}.");

        CardDetails? card = instruction.Card;
        string? vaultId = null;
        string? customerId = null;

        if (instruction.SavedPaymentMethodId.HasValue)
        {
            var saved = await _savedCardRepository.GetByIdAsync(instruction.SavedPaymentMethodId.Value, ct);
            if (saved is null || saved.BuyerId != buyerId)
                throw new PaymentNotFoundException("Saved card not found.");
            vaultId = saved.PayPalVaultId;
            customerId = saved.PayPalCustomerId;
            card = null;
        }
        else if (card is null)
        {
            throw new PaymentValidationException("Provide either card details or a savedPaymentMethodId.");
        }

        var request = new AuthorizeCardRequest(
            Amount: payment.Amount,
            OrderReference: orderId.ToString(),
            IdempotencyKey: $"pay-{orderId}",
            Card: card,
            VaultId: vaultId,
            PayPalCustomerId: customerId);

        try
        {
            var result = await _gateway.AuthorizeAsync(request, ct);
            payment.MarkAuthorized(result.PayPalOrderId, result.AuthorizationId, result.Status,
                result.ExpiresAt, result.CardBrand, result.CardLast4);
            await _paymentRepository.UpdateAsync(payment, ct);
            _logger.LogInformation($"Order {orderId} authorized: paypalOrder={result.PayPalOrderId} auth={result.AuthorizationId} status={result.Status}.");
            return payment;
        }
        catch (PaymentGatewayException ex)
        {
            payment.MarkFailed($"Authorization failed: {ex.Message}");
            await _paymentRepository.UpdateAsync(payment, ct);
            throw;
        }
    }

    public async Task<OrderPayment> FulfilAsync(int orderId, CancellationToken ct)
    {
        var payment = await LoadPaymentAsync(orderId, ct);

        if (payment.Status == OrderPaymentStatus.Fulfilled)
            return payment; // idempotent
        if (payment.Status != OrderPaymentStatus.Authorized || string.IsNullOrEmpty(payment.AuthorizationId))
            throw new PaymentConflictException($"Order {orderId} cannot be fulfilled in state {payment.Status}.");

        var capture = await CaptureWithRenewalAsync(payment, ct);
        payment.MarkFulfilled(capture.CaptureId, capture.Status, capture.GrossAmount, capture.PayPalFee, capture.NetAmount);
        await _paymentRepository.UpdateAsync(payment, ct);
        _logger.LogInformation($"Order {orderId} fulfilled: capture={capture.CaptureId} gross={capture.GrossAmount} fee={capture.PayPalFee} net={capture.NetAmount}.");
        return payment;
    }

    private async Task<PayPalCaptureResult> CaptureWithRenewalAsync(OrderPayment payment, CancellationToken ct)
    {
        var renewed = false;

        // Proactively renew a hold already past its expiry before attempting capture.
        var stale = payment.AuthorizationExpiresAt is DateTimeOffset exp && exp <= DateTimeOffset.UtcNow;
        if (stale)
        {
            await RenewAuthorizationAsync(payment, ct);
            renewed = true;
        }

        try
        {
            return await _gateway.CaptureAsync(payment.AuthorizationId!, amount: null, $"cap-{payment.Id}", ct);
        }
        catch (PaymentGatewayException ex) when (ex.AuthorizationExpired && !renewed)
        {
            // The hold went stale between authorization and fulfilment: renew, then capture again.
            await RenewAuthorizationAsync(payment, ct);
            return await _gateway.CaptureAsync(payment.AuthorizationId!, amount: null, $"cap-{payment.Id}-renewed", ct);
        }
    }

    private async Task RenewAuthorizationAsync(OrderPayment payment, CancellationToken ct)
    {
        try
        {
            var reauth = await _gateway.ReauthorizeAsync(payment.AuthorizationId!, payment.Amount, $"reauth-{payment.Id}", ct);
            payment.RenewAuthorization(reauth.AuthorizationId, reauth.Status, reauth.ExpiresAt);
            await _paymentRepository.UpdateAsync(payment, ct);
            _logger.LogInformation($"Order {payment.OrderId} authorization renewed: auth={reauth.AuthorizationId}.");
        }
        catch (PaymentGatewayException ex)
        {
            var message = $"The authorization for order {payment.OrderId} has expired and can no longer be renewed " +
                          $"({ex.Message}). Ask the shopper to pay again to create a new authorization.";
            payment.MarkFailed(message);
            await _paymentRepository.UpdateAsync(payment, ct);
            throw new PaymentConflictException(message);
        }
    }

    public async Task<OrderPayment> CancelAsync(int orderId, CancellationToken ct)
    {
        var payment = await LoadPaymentAsync(orderId, ct);

        if (payment.Status == OrderPaymentStatus.Canceled)
            return payment; // idempotent
        if (payment.Status != OrderPaymentStatus.Authorized || string.IsNullOrEmpty(payment.AuthorizationId))
            throw new PaymentConflictException($"Order {orderId} cannot be cancelled in state {payment.Status}. " +
                (payment.Status == OrderPaymentStatus.Fulfilled ? "It is already fulfilled — issue a refund instead." : string.Empty));

        await _gateway.VoidAsync(payment.AuthorizationId!, $"void-{payment.Id}", ct);
        payment.MarkCanceled();
        await _paymentRepository.UpdateAsync(payment, ct);
        _logger.LogInformation($"Order {orderId} cancelled; hold released.");
        return payment;
    }

    public async Task<RefundOutcome> RefundAsync(string buyerId, int orderId, decimal? amount, string idempotencyKey, string? note, CancellationToken ct)
    {
        Guard.Against.NullOrEmpty(idempotencyKey, nameof(idempotencyKey));
        var payment = await LoadOwnedPaymentAsync(buyerId, orderId, ct);

        if (payment.Status is not (OrderPaymentStatus.Fulfilled or OrderPaymentStatus.PartiallyRefunded)
            || string.IsNullOrEmpty(payment.CaptureId))
            throw new PaymentConflictException($"Order {orderId} has no captured payment to refund (state {payment.Status}).");

        // Idempotent: repeating a request under the same key must not refund twice.
        var existing = payment.FindRefundByKey(idempotencyKey);
        if (existing is not null)
            return new RefundOutcome(existing, payment);

        var remaining = payment.RefundableRemaining();
        if (remaining <= 0m)
            throw new PaymentConflictException($"Order {orderId} is already fully refunded.");

        var effective = amount ?? remaining;
        if (effective <= 0m)
            throw new PaymentValidationException("Refund amount must be greater than zero.");
        // A partly-refunded order must never become refundable beyond what was captured.
        if (effective > remaining)
            throw new PaymentValidationException($"Refund amount {effective} exceeds the refundable remaining {remaining}.");

        var result = await _gateway.RefundAsync(payment.CaptureId!, effective, idempotencyKey, note, orderId.ToString(), ct);
        var refund = new PaymentRefund(result.RefundId, result.Amount, result.Status ?? "UNKNOWN", idempotencyKey);
        payment.AddRefund(refund);
        await _paymentRepository.UpdateAsync(payment, ct);
        _logger.LogInformation($"Order {orderId} refunded {result.Amount}: refund={result.RefundId} status={result.Status}.");
        return new RefundOutcome(refund, payment);
    }

    public async Task<IReadOnlyList<MyOrderView>> GetMyOrdersAsync(string buyerId, CancellationToken ct)
    {
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));
        var payments = await _paymentRepository.ListAsync(new OrderPaymentsByBuyerSpecification(buyerId), ct);
        var orders = await _orderRepository.ListAsync(new CustomerOrdersWithItemsSpecification(buyerId), ct);
        var orderById = orders.ToDictionary(o => o.Id);

        return payments
            .OrderByDescending(p => p.OrderId)
            .Select(p =>
            {
                orderById.TryGetValue(p.OrderId, out var order);
                return new MyOrderView(
                    OrderId: p.OrderId,
                    OrderDate: order?.OrderDate ?? p.CreatedDate,
                    Total: order?.Total() ?? p.Amount,
                    Currency: p.CurrencyCode,
                    Status: p.Status,
                    PayPalOrderId: p.PayPalOrderId,
                    AuthorizationId: p.AuthorizationId,
                    AuthorizationStatus: p.AuthorizationStatus,
                    AuthorizationExpiresAt: p.AuthorizationExpiresAt,
                    CaptureId: p.CaptureId,
                    CapturedAmount: p.CapturedAmount,
                    PayPalFee: p.PayPalFee,
                    NetAmount: p.NetAmount,
                    CardBrand: p.CardBrand,
                    CardLast4: p.CardLast4,
                    TotalRefunded: p.TotalRefunded(),
                    RefundableRemaining: p.RefundableRemaining(),
                    Refunds: p.Refunds
                        .Select(r => new RefundView(r.PayPalRefundId, r.Amount, r.Status, r.CreatedDate))
                        .ToList());
            })
            .ToList();
    }

    public async Task<SavedPaymentMethod> SavePaymentMethodAsync(string buyerId, CardDetails card, CancellationToken ct)
    {
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));
        Guard.Against.Null(card, nameof(card));

        // Reuse the shopper's existing PayPal customer id so their vaulted cards group together.
        var existing = await _savedCardRepository.ListAsync(new SavedPaymentMethodsByBuyerSpecification(buyerId), ct);
        var customerId = existing.FirstOrDefault(m => m.PayPalCustomerId is not null)?.PayPalCustomerId;

        var request = new VaultCardRequest(card, customerId, Guid.NewGuid().ToString("N"));
        var result = await _gateway.VaultCardAsync(request, ct);

        var method = new SavedPaymentMethod(
            buyerId,
            result.VaultId,
            result.CustomerId ?? customerId,
            result.Brand,
            result.Last4,
            result.Expiry,
            result.CardholderName ?? card.CardholderName);
        await _savedCardRepository.AddAsync(method, ct);
        _logger.LogInformation($"Saved card {method.Id} for {buyerId}: {result.Brand} ****{result.Last4}.");
        return method;
    }

    public async Task<IReadOnlyList<SavedPaymentMethod>> GetPaymentMethodsAsync(string buyerId, CancellationToken ct)
    {
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));
        return await _savedCardRepository.ListAsync(new SavedPaymentMethodsByBuyerSpecification(buyerId), ct);
    }

    public async Task DeletePaymentMethodAsync(string buyerId, int paymentMethodId, CancellationToken ct)
    {
        var method = await _savedCardRepository.GetByIdAsync(paymentMethodId, ct);
        if (method is null || method.BuyerId != buyerId)
            throw new PaymentNotFoundException("Saved card not found.");

        await _gateway.DeleteVaultedCardAsync(method.PayPalVaultId, ct);
        await _savedCardRepository.DeleteAsync(method, ct);
        _logger.LogInformation($"Deleted saved card {paymentMethodId} for {buyerId}.");
    }

    public async Task<ReconciliationReport> ReconcileAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken ct)
    {
        if (to < from)
            throw new PaymentValidationException("'to' must be on or after 'from'.");

        var transactions = await _gateway.SearchTransactionsAsync(from, to, ct);

        // eShop side: payments captured whose order was placed within the range.
        var allPayments = await _paymentRepository.ListAsync(ct);
        var eShopCaptured = allPayments
            .Where(p => !string.IsNullOrEmpty(p.CaptureId) && p.CreatedDate >= from && p.CreatedDate <= to)
            .ToList();

        var eShopByOrderId = eShopCaptured.ToDictionary(p => p.OrderId);
        var matchedOrderIds = new HashSet<int>();

        var matched = new List<ReconciliationEntry>();
        var inPayPalNotEShop = new List<ReconciliationEntry>();

        foreach (var t in transactions)
        {
            var reference = t.InvoiceId ?? t.CustomField;
            if (reference is not null && int.TryParse(reference, out var orderId) && eShopByOrderId.TryGetValue(orderId, out var p))
            {
                matchedOrderIds.Add(orderId);
                matched.Add(new ReconciliationEntry(orderId, t.TransactionId, p.PayPalOrderId, p.CaptureId,
                    t.Amount, t.CurrencyCode ?? p.CurrencyCode, t.Status, t.Date));
            }
            else
            {
                inPayPalNotEShop.Add(new ReconciliationEntry(null, t.TransactionId, null, null,
                    t.Amount, t.CurrencyCode, t.Status, t.Date));
            }
        }

        var inEShopNotPayPal = eShopCaptured
            .Where(p => !matchedOrderIds.Contains(p.OrderId))
            .Select(p => new ReconciliationEntry(p.OrderId, null, p.PayPalOrderId, p.CaptureId,
                p.CapturedAmount, p.CurrencyCode, p.CaptureStatus, p.UpdatedDate))
            .ToList();

        return new ReconciliationReport(from, to, transactions.Count, matched, inPayPalNotEShop, inEShopNotPayPal);
    }

    private async Task<OrderPayment> LoadPaymentAsync(int orderId, CancellationToken ct)
    {
        var payment = await _paymentRepository.FirstOrDefaultAsync(new OrderPaymentByOrderIdSpecification(orderId), ct);
        if (payment is null)
            throw new PaymentNotFoundException($"Order {orderId} not found.");
        return payment;
    }

    private async Task<OrderPayment> LoadOwnedPaymentAsync(string buyerId, int orderId, CancellationToken ct)
    {
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));
        var payment = await LoadPaymentAsync(orderId, ct);
        // One shopper must never see or act on another's order.
        if (payment.BuyerId != buyerId)
            throw new PaymentNotFoundException($"Order {orderId} not found.");
        return payment;
    }
}
