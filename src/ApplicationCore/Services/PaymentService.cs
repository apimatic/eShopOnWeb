using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Configuration;
using Microsoft.eShopWeb.ApplicationCore.Entities;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentMethodAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Interfaces.Payments;
using Microsoft.eShopWeb.ApplicationCore.Specifications;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.ApplicationCore.Services;

public class PaymentService : IPaymentService
{
    private static readonly Regex ExpiryPattern = new(@"^\d{4}-(0[1-9]|1[0-2])$", RegexOptions.Compiled);

    // Orders in this API are placed without a storefront checkout, so no shipping address is collected.
    // A placeholder ship-to keeps the existing Order invariant satisfied without inventing a new model.
    private static readonly Func<Address> DefaultShipToAddress =
        () => new Address("N/A", "N/A", "N/A", "N/A", "00000");

    private readonly IRepository<Order> _orderRepository;
    private readonly IRepository<CatalogItem> _itemRepository;
    private readonly IRepository<SavedPaymentMethod> _paymentMethodRepository;
    private readonly IPayPalGateway _gateway;
    private readonly IUriComposer _uriComposer;
    private readonly PayPalSettings _settings;

    public PaymentService(
        IRepository<Order> orderRepository,
        IRepository<CatalogItem> itemRepository,
        IRepository<SavedPaymentMethod> paymentMethodRepository,
        IPayPalGateway gateway,
        IUriComposer uriComposer,
        IOptions<PayPalSettings> settings)
    {
        _orderRepository = orderRepository;
        _itemRepository = itemRepository;
        _paymentMethodRepository = paymentMethodRepository;
        _gateway = gateway;
        _uriComposer = uriComposer;
        _settings = settings.Value;
    }

    private string Currency => _settings.Currency;

    public async Task<int> PlaceOrderAsync(string buyerId, IEnumerable<OrderLineRequest> lines, CancellationToken ct)
    {
        var requested = (lines ?? Enumerable.Empty<OrderLineRequest>()).ToList();
        if (requested.Count == 0)
        {
            throw new PaymentException("An order must contain at least one item.");
        }
        if (requested.Any(l => l.Quantity <= 0))
        {
            throw new PaymentException("Every order line must have a quantity greater than zero.");
        }

        var ids = requested.Select(l => l.CatalogItemId).Distinct().ToArray();
        var catalogItems = await _itemRepository.ListAsync(new CatalogItemsSpecification(ids), ct);

        var orderItems = new List<OrderItem>();
        foreach (var line in requested)
        {
            var catalogItem = catalogItems.FirstOrDefault(c => c.Id == line.CatalogItemId)
                ?? throw new PaymentException($"Catalog item {line.CatalogItemId} was not found.");

            // Amounts come from catalog prices, not from the caller.
            var itemOrdered = new CatalogItemOrdered(catalogItem.Id, catalogItem.Name,
                _uriComposer.ComposePicUri(catalogItem.PictureUri));
            orderItems.Add(new OrderItem(itemOrdered, catalogItem.Price, line.Quantity));
        }

        var order = new Order(buyerId, DefaultShipToAddress(), orderItems);
        order = await _orderRepository.AddAsync(order, ct);
        return order.Id;
    }

    public async Task PayOrderAsync(string buyerId, int orderId, PaymentInstrument instrument, CancellationToken ct)
    {
        var order = await LoadOwnedOrderAsync(buyerId, orderId, ct);

        // Idempotent in effect: a second click on an already-authorized order is a no-op.
        if (order.PaymentStatus == OrderPaymentStatus.Authorized)
        {
            return;
        }

        var amount = order.Total();
        // Stable per order (so a double-click reuses the same PayPal-Request-Id and does not authorize twice)
        // yet globally unique across orders — OrderDate differs per order, including across app restarts.
        var idempotencyKey = $"authorize-{order.Id}-{order.OrderDate.Ticks}";

        AuthorizationResult authorization;
        string description;
        bool usedSavedCard;

        if (instrument.SavedPaymentMethodId.HasValue)
        {
            var saved = await _paymentMethodRepository.FirstOrDefaultAsync(
                new SavedPaymentMethodByIdSpecification(instrument.SavedPaymentMethodId.Value, buyerId), ct)
                ?? throw new PaymentNotFoundException("Saved card not found.");

            authorization = await _gateway.AuthorizeWithVaultedCardAsync(amount, Currency, saved.PayPalVaultId, idempotencyKey, ct);
            description = DescribeCard(saved.CardBrand, saved.LastFourDigits);
            usedSavedCard = true;
        }
        else if (instrument.Card is not null)
        {
            ValidateCard(instrument.Card);
            authorization = await _gateway.AuthorizeWithCardAsync(amount, Currency, instrument.Card, idempotencyKey, ct);
            description = DescribeCard(null, LastFour(instrument.Card.Number));
            usedSavedCard = false;
        }
        else
        {
            throw new PaymentException("Provide either card details or a saved card id to pay.");
        }

        var payment = new OrderPayment(
            authorization.PayPalOrderId,
            authorization.AuthorizationId,
            authorization.Status,
            authorization.ExpiresAt,
            Currency,
            amount,
            description,
            usedSavedCard);

        order.RecordAuthorization(payment);
        await _orderRepository.UpdateAsync(order, ct);
    }

    public async Task FulfilOrderAsync(int orderId, CancellationToken ct)
    {
        var order = await LoadOrderAsync(orderId, ct);

        // Idempotent: fulfilling an already-fulfilled order does nothing.
        if (order.PaymentStatus == OrderPaymentStatus.Fulfilled)
        {
            return;
        }
        if (order.PaymentStatus != OrderPaymentStatus.Authorized || order.Payment is null)
        {
            throw new PaymentException($"Order {orderId} cannot be fulfilled because it is {order.PaymentStatus}.");
        }

        var payment = order.Payment;
        var authorizationId = payment.AuthorizationId;

        // If the hold has gone stale, renew it rather than failing the fulfilment.
        var state = await _gateway.GetAuthorizationAsync(authorizationId, ct);
        if (IsStale(state))
        {
            // Throws PaymentReauthorizationException (operator-actionable) when it can no longer be renewed.
            var reauthorized = await _gateway.ReauthorizeAsync(authorizationId, payment.AuthorizedAmount, payment.Currency, ct);
            payment.RenewAuthorization(reauthorized.AuthorizationId, reauthorized.Status, reauthorized.ExpiresAt);
            authorizationId = reauthorized.AuthorizationId;
        }

        // Keyed on the authorization actually being captured (globally unique, stable across a retried
        // fulfilment) so a double-fulfil does not capture twice.
        var captureKey = $"capture-{authorizationId}";
        var capture = await _gateway.CaptureAsync(authorizationId, captureKey, ct);
        payment.RecordCapture(capture.CaptureId, capture.Status, capture.GrossAmount, capture.PayPalFee, capture.NetAmount);

        order.RecordFulfilment();
        await _orderRepository.UpdateAsync(order, ct);
    }

    public async Task CancelOrderAsync(int orderId, CancellationToken ct)
    {
        var order = await LoadOrderAsync(orderId, ct);

        if (order.PaymentStatus == OrderPaymentStatus.Cancelled)
        {
            return;
        }
        if (order.PaymentStatus != OrderPaymentStatus.Authorized || order.Payment is null)
        {
            throw new PaymentException($"Order {orderId} cannot be cancelled because it is {order.PaymentStatus}.");
        }

        await _gateway.VoidAuthorizationAsync(order.Payment.AuthorizationId, ct);
        order.RecordCancellation();
        await _orderRepository.UpdateAsync(order, ct);
    }

    public async Task<int> RefundOrderAsync(string buyerId, int orderId, decimal? amount, string idempotencyKey, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(idempotencyKey))
        {
            throw new PaymentException("A refund requires an idempotency key.");
        }

        var order = await LoadOwnedOrderAsync(buyerId, orderId, ct);
        var payment = order.Payment;
        if (payment is null || payment.CaptureId is null)
        {
            throw new PaymentException($"Order {orderId} has no captured payment to refund.");
        }

        // Repeating a refund under the same key must not refund twice.
        var existing = payment.FindRefundByIdempotencyKey(idempotencyKey);
        if (existing is not null)
        {
            return existing.Id;
        }

        var remaining = payment.RefundableRemaining();
        if (remaining <= 0m)
        {
            throw new PaymentException($"Order {orderId} has already been fully refunded.");
        }
        if (amount.HasValue)
        {
            if (amount.Value <= 0m)
            {
                throw new PaymentException("A partial refund amount must be greater than zero.");
            }
            if (amount.Value > remaining)
            {
                throw new PaymentException(
                    $"Refund of {amount.Value:0.00} exceeds the remaining refundable amount of {remaining:0.00}.");
            }
        }

        var result = await _gateway.RefundAsync(payment.CaptureId, amount, payment.Currency, idempotencyKey, ct);
        var refund = new PaymentRefund(result.RefundId, result.Status, result.Amount, result.Currency, idempotencyKey);
        order.RecordRefund(refund);
        await _orderRepository.UpdateAsync(order, ct);
        return refund.Id;
    }

    public async Task<IReadOnlyList<OrderPaymentSummary>> GetMyOrdersAsync(string buyerId, CancellationToken ct)
    {
        var orders = await _orderRepository.ListAsync(new CustomerOrdersWithPaymentSpecification(buyerId), ct);
        return orders.Select(ToSummary).ToList();
    }

    public async Task<ReconciliationReport> GetReconciliationAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken ct)
    {
        var transactions = await _gateway.SearchTransactionsAsync(from, to, ct);

        var paidOrders = await _orderRepository.ListAsync(new PaidOrdersSpecification(), ct);
        var eShopCaptures = paidOrders
            .Where(o => o.Payment?.CaptureId is not null)
            .Select(o => (Order: o, Payment: o.Payment!))
            .ToList();

        var eShopByCaptureId = eShopCaptures
            .GroupBy(x => x.Payment.CaptureId!)
            .ToDictionary(g => g.Key, g => g.First());

        var payPalIds = new HashSet<string>(transactions.Select(t => t.TransactionId));

        var matched = new List<ReconciliationMatch>();
        var missingInEShop = new List<ReconciliationTransaction>();
        foreach (var tx in transactions)
        {
            if (eShopByCaptureId.TryGetValue(tx.TransactionId, out var eShop))
            {
                matched.Add(new ReconciliationMatch(
                    eShop.Order.Id,
                    tx.TransactionId,
                    eShop.Payment.CapturedGrossAmount ?? 0m,
                    tx.Amount,
                    tx.Status));
            }
            else
            {
                missingInEShop.Add(tx);
            }
        }

        // eShop captures that fall within the range but PayPal has not reported.
        var missingInPayPal = eShopCaptures
            .Where(x => x.Payment.CapturedAt is not null
                        && x.Payment.CapturedAt >= from && x.Payment.CapturedAt <= to
                        && !payPalIds.Contains(x.Payment.CaptureId!))
            .Select(x => new ReconciliationEShopEntry(
                x.Order.Id,
                x.Payment.CaptureId!,
                x.Payment.CapturedGrossAmount ?? 0m,
                x.Payment.Currency,
                x.Payment.CapturedAt))
            .ToList();

        return new ReconciliationReport(from, to, matched, missingInEShop, missingInPayPal);
    }

    public async Task<int> SavePaymentMethodAsync(string buyerId, CardDetails card, CancellationToken ct)
    {
        ValidateCard(card);
        var vaulted = await _gateway.VaultCardAsync(card, buyerId, ct);
        var saved = new SavedPaymentMethod(buyerId, vaulted.VaultId, vaulted.CardBrand, vaulted.LastFourDigits, vaulted.Expiry);
        saved = await _paymentMethodRepository.AddAsync(saved, ct);
        return saved.Id;
    }

    public async Task<IReadOnlyList<SavedPaymentMethodSummary>> GetPaymentMethodsAsync(string buyerId, CancellationToken ct)
    {
        var methods = await _paymentMethodRepository.ListAsync(new CustomerSavedPaymentMethodsSpecification(buyerId), ct);
        return methods
            .Select(m => new SavedPaymentMethodSummary(m.Id, m.CardBrand, m.LastFourDigits, m.Expiry, m.CreatedAt))
            .ToList();
    }

    public async Task DeletePaymentMethodAsync(string buyerId, int paymentMethodId, CancellationToken ct)
    {
        var saved = await _paymentMethodRepository.FirstOrDefaultAsync(
            new SavedPaymentMethodByIdSpecification(paymentMethodId, buyerId), ct)
            ?? throw new PaymentNotFoundException("Saved card not found.");

        // Delete the vault token first so the card can no longer be used to pay, then drop the local record.
        await _gateway.DeleteVaultedCardAsync(saved.PayPalVaultId, ct);
        await _paymentMethodRepository.DeleteAsync(saved, ct);
    }

    // --- helpers ---

    private async Task<Order> LoadOrderAsync(int orderId, CancellationToken ct)
    {
        return await _orderRepository.FirstOrDefaultAsync(new OrderWithPaymentByIdSpec(orderId), ct)
            ?? throw new PaymentNotFoundException("Order not found.");
    }

    private async Task<Order> LoadOwnedOrderAsync(string buyerId, int orderId, CancellationToken ct)
    {
        var order = await _orderRepository.FirstOrDefaultAsync(new OrderWithPaymentByIdSpec(orderId), ct);
        if (order is null || !string.Equals(order.BuyerId, buyerId, StringComparison.Ordinal))
        {
            // Same response whether it is missing or someone else's — ownership is never leaked.
            throw new PaymentNotFoundException("Order not found.");
        }
        return order;
    }

    private static bool IsStale(AuthorizationState state) =>
        state.ExpiresAt.HasValue && state.ExpiresAt.Value <= DateTimeOffset.Now;

    private OrderPaymentSummary ToSummary(Order order)
    {
        var items = order.OrderItems
            .Select(i => new OrderLineView(i.ItemOrdered.CatalogItemId, i.ItemOrdered.ProductName, i.UnitPrice, i.Units))
            .ToList();

        PaymentStateView? paymentView = null;
        var p = order.Payment;
        if (p is not null)
        {
            var refunds = p.Refunds
                .Select(r => new RefundView(r.Id, r.PayPalRefundId, r.Status, r.Amount, r.Currency))
                .ToList();

            paymentView = new PaymentStateView(
                p.PayPalOrderId, p.AuthorizationId, p.AuthorizationStatus, p.AuthorizationExpiresAt,
                p.AuthorizedAmount, p.PaymentMethodDescription, p.UsedSavedCard,
                p.CaptureId, p.CaptureStatus, p.CapturedGrossAmount, p.PayPalFee, p.NetAmount,
                p.TotalRefunded(), refunds);
        }

        return new OrderPaymentSummary(
            order.Id, order.OrderDate, order.Total(), Currency,
            order.PaymentStatus.ToString(), paymentView, items);
    }

    private static string DescribeCard(string? brand, string lastFour)
    {
        var label = string.IsNullOrWhiteSpace(brand) ? "Card" : brand;
        return $"{label} ****{lastFour}";
    }

    private static string LastFour(string number)
    {
        var digits = new string((number ?? string.Empty).Where(char.IsDigit).ToArray());
        return digits.Length >= 4 ? digits[^4..] : digits;
    }

    private static void ValidateCard(CardDetails card)
    {
        if (card is null)
        {
            throw new PaymentException("Card details are required.");
        }

        var digits = new string((card.Number ?? string.Empty).Where(char.IsDigit).ToArray());
        if (digits.Length < 12 || digits.Length > 19)
        {
            throw new PaymentException("A valid card number is required.");
        }
        if (string.IsNullOrWhiteSpace(card.Expiry) || !ExpiryPattern.IsMatch(card.Expiry))
        {
            throw new PaymentException("Card expiry must be in YYYY-MM format.");
        }
        if (string.IsNullOrWhiteSpace(card.SecurityCode))
        {
            throw new PaymentException("A card security code is required.");
        }
        if (card.BillingAddress is null || string.IsNullOrWhiteSpace(card.BillingAddress.CountryCode))
        {
            throw new PaymentException("A billing address with a country code is required.");
        }
    }
}
