using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Ardalis.GuardClauses;
using Microsoft.eShopWeb.ApplicationCore.Entities;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentMethodAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Payments;
using Microsoft.eShopWeb.ApplicationCore.Specifications;

namespace Microsoft.eShopWeb.ApplicationCore.Services;

public class OrderPaymentService : IOrderPaymentService
{
    // Stable for the life of the process: makes the PayPal invoice_id we send unique across
    // app runs (the in-memory DB restarts order ids at 1) while staying deterministic within
    // a run, so PayPal-Request-Id-based idempotency still returns the same order on a retry.
    private static readonly string RunId = Guid.NewGuid().ToString("N").Substring(0, 8);

    private readonly IRepository<Order> _orderRepository;
    private readonly IRepository<CatalogItem> _itemRepository;
    private readonly IReadRepository<SavedPaymentMethod> _savedCardRepository;
    private readonly IPayPalGateway _payPal;
    private readonly IUriComposer _uriComposer;
    private readonly PaymentSettings _settings;
    private readonly IAppLogger<OrderPaymentService> _logger;

    public OrderPaymentService(
        IRepository<Order> orderRepository,
        IRepository<CatalogItem> itemRepository,
        IReadRepository<SavedPaymentMethod> savedCardRepository,
        IPayPalGateway payPal,
        IUriComposer uriComposer,
        PaymentSettings settings,
        IAppLogger<OrderPaymentService> logger)
    {
        _orderRepository = orderRepository;
        _itemRepository = itemRepository;
        _savedCardRepository = savedCardRepository;
        _payPal = payPal;
        _uriComposer = uriComposer;
        _settings = settings;
        _logger = logger;
    }

    public async Task<Order> PlaceOrderAsync(
        string buyerId, IReadOnlyCollection<OrderLine> lines, Address shipToAddress,
        CancellationToken cancellationToken = default)
    {
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));
        Guard.Against.Null(shipToAddress, nameof(shipToAddress));
        if (lines is null || lines.Count == 0)
        {
            throw new ArgumentException("An order must contain at least one line.", nameof(lines));
        }

        // Merge duplicate catalog ids, and reject non-positive quantities.
        var mergedQuantities = new Dictionary<int, int>();
        foreach (var line in lines)
        {
            if (line.Quantity <= 0)
            {
                throw new ArgumentException($"Quantity for catalog item {line.CatalogItemId} must be positive.", nameof(lines));
            }
            mergedQuantities[line.CatalogItemId] = mergedQuantities.GetValueOrDefault(line.CatalogItemId) + line.Quantity;
        }

        var catalogItems = await _itemRepository.ListAsync(
            new CatalogItemsSpecification(mergedQuantities.Keys.ToArray()), cancellationToken);

        var items = mergedQuantities.Select(kvp =>
        {
            var catalogItem = catalogItems.FirstOrDefault(c => c.Id == kvp.Key)
                ?? throw new ArgumentException($"Catalog item {kvp.Key} does not exist.", nameof(lines));
            var pictureUri = _uriComposer.ComposePicUri(catalogItem.PictureUri ?? string.Empty);
            if (string.IsNullOrEmpty(pictureUri)) pictureUri = "no-image";
            var itemOrdered = new CatalogItemOrdered(catalogItem.Id, catalogItem.Name, pictureUri);
            return new OrderItem(itemOrdered, catalogItem.Price, kvp.Value);
        }).ToList();

        var order = new Order(buyerId, shipToAddress, items);
        await _orderRepository.AddAsync(order, cancellationToken);
        _logger.LogInformation("Placed order {0} for buyer {1} with total {2}", order.Id, buyerId, order.Total());
        return order;
    }

    public async Task<Order> AuthorizePaymentAsync(
        string buyerId, int orderId, PaymentInstrument instrument,
        CancellationToken cancellationToken = default)
    {
        var order = await LoadOwnedOrderAsync(buyerId, orderId, cancellationToken);

        // Idempotent in effect: a double-click that arrives after the hold is placed is a no-op.
        if (order.Payment is not null)
        {
            if (order.Payment.Status == PaymentStatus.Authorized)
            {
                return order;
            }
            throw new PaymentStateException($"Order {orderId} has already been paid; its payment status is {order.Payment.Status}.");
        }

        ValidateInstrument(instrument);

        var amount = decimal.Round(order.Total(), 2, MidpointRounding.AwayFromZero);
        if (amount <= 0)
        {
            throw new PaymentStateException($"Order {orderId} has a non-positive total and cannot be paid.");
        }
        var currency = _settings.Currency;
        var invoiceId = $"ESHOP-{RunId}-{orderId}";
        var customId = orderId.ToString(CultureInfo.InvariantCulture);

        // Deterministic per order → PayPal de-duplicates concurrent/duplicate attempts.
        var createRequestId = $"eshop-order-{RunId}-{orderId}";
        var authorizeRequestId = $"eshop-authorize-{RunId}-{orderId}";

        var ppOrder = await _payPal.CreateAuthorizationOrderAsync(
            amount, currency, invoiceId, customId, createRequestId, cancellationToken);

        string? vaultId = null;
        int? savedPaymentMethodId = null;
        string cardDescriptor;

        if (instrument.SavedPaymentMethodId is int savedId)
        {
            var saved = await _savedCardRepository.FirstOrDefaultAsync(
                new SavedPaymentMethodByIdSpecification(savedId, buyerId), cancellationToken)
                ?? throw new PaymentMethodNotFoundException(savedId);
            vaultId = saved.VaultId;
            savedPaymentMethodId = saved.Id;
            cardDescriptor = DescribeCard(saved.CardBrand, saved.Last4);
        }
        else
        {
            cardDescriptor = DescribeCard("Card", Last4Of(instrument.Card!.Number));
        }

        var authorization = vaultId is not null
            ? await _payPal.AuthorizeOrderWithVaultAsync(ppOrder.OrderId, vaultId, authorizeRequestId, cancellationToken)
            : await _payPal.AuthorizeOrderWithCardAsync(ppOrder.OrderId, instrument.Card!, authorizeRequestId, cancellationToken);

        var payment = new OrderPayment(
            currency, amount, ppOrder.OrderId, invoiceId,
            authorization.AuthorizationId, authorization.Status, authorization.ExpiresAt,
            savedPaymentMethodId, cardDescriptor);

        order.AttachPayment(payment);
        await _orderRepository.UpdateAsync(order, cancellationToken);
        _logger.LogInformation("Authorized {0} {1} on order {2} (authorization {3})",
            amount, currency, orderId, authorization.AuthorizationId);
        return order;
    }

    public async Task<Order> FulfilOrderAsync(int orderId, CancellationToken cancellationToken = default)
    {
        var order = await LoadOrderAsync(orderId, cancellationToken);
        var payment = order.Payment
            ?? throw new PaymentStateException($"Order {orderId} is awaiting payment; there is nothing to fulfil.");

        if (payment.Status == PaymentStatus.Captured)
        {
            return order; // already fulfilled — idempotent
        }
        if (payment.Status != PaymentStatus.Authorized)
        {
            throw new PaymentStateException($"Order {orderId} cannot be fulfilled from status {payment.Status}.");
        }

        var captureRequestId = $"eshop-capture-{RunId}-{orderId}";

        // If the hold has gone stale before fulfilment, renew it rather than failing outright.
        var authorizationId = await EnsureFreshAuthorizationAsync(order, cancellationToken);

        PayPalCaptureResult capture;
        try
        {
            capture = await _payPal.CaptureAuthorizationAsync(
                authorizationId, payment.InvoiceId, captureRequestId, cancellationToken);
        }
        catch (PayPalApiException ex) when (IsExpiredAuthorization(ex))
        {
            _logger.LogWarning("Authorization {0} for order {1} expired at capture ({2}); attempting to renew.",
                authorizationId, orderId, ex.Issue);
            authorizationId = await RenewAuthorizationOrThrowAsync(order, cancellationToken);
            capture = await _payPal.CaptureAuthorizationAsync(
                authorizationId, payment.InvoiceId, captureRequestId, cancellationToken);
        }

        payment.RecordCapture(capture.CaptureId, capture.Status, capture.GrossAmount, capture.PayPalFee, capture.NetAmount);
        await _orderRepository.UpdateAsync(order, cancellationToken);
        _logger.LogInformation("Captured order {0}: gross {1}, fee {2}, net {3} (capture {4})",
            orderId, capture.GrossAmount, capture.PayPalFee, capture.NetAmount, capture.CaptureId);
        return order;
    }

    public async Task<Order> CancelOrderAsync(int orderId, CancellationToken cancellationToken = default)
    {
        var order = await LoadOrderAsync(orderId, cancellationToken);
        var payment = order.Payment
            ?? throw new PaymentStateException($"Order {orderId} is awaiting payment; there is no hold to release.");

        if (payment.Status == PaymentStatus.Voided)
        {
            return order; // already cancelled — idempotent
        }
        if (payment.Status != PaymentStatus.Authorized)
        {
            throw new PaymentStateException(
                $"Order {orderId} cannot be cancelled from status {payment.Status}; a captured order must be refunded instead.");
        }

        await _payPal.VoidAuthorizationAsync(payment.AuthorizationId, cancellationToken);
        payment.RecordVoid();
        await _orderRepository.UpdateAsync(order, cancellationToken);
        _logger.LogInformation("Cancelled order {0}; released authorization {1}", orderId, payment.AuthorizationId);
        return order;
    }

    public async Task<PaymentRefund> RefundOrderAsync(
        string buyerId, int orderId, decimal? amount, string idempotencyKey,
        CancellationToken cancellationToken = default)
    {
        Guard.Against.NullOrEmpty(idempotencyKey, nameof(idempotencyKey));
        var order = await LoadOwnedOrderAsync(buyerId, orderId, cancellationToken);
        var payment = order.Payment
            ?? throw new PaymentStateException($"Order {orderId} has no captured payment to refund.");

        if (payment.Status != PaymentStatus.Captured && payment.Status != PaymentStatus.PartiallyRefunded)
        {
            throw new PaymentStateException($"Order {orderId} cannot be refunded from status {payment.Status}.");
        }

        // Idempotent per caller key: repeating the same key never refunds twice.
        var existing = payment.FindRefundByIdempotencyKey(idempotencyKey);
        if (existing is not null)
        {
            return existing;
        }

        if (amount is decimal requested)
        {
            if (requested <= 0)
            {
                throw new PaymentStateException("A partial refund amount must be positive.");
            }
            if (requested > payment.RefundableRemaining)
            {
                throw new PaymentStateException(
                    $"Refund of {requested:0.00} exceeds the refundable remaining {payment.RefundableRemaining:0.00} {payment.CurrencyCode} on order {orderId}.");
            }
        }

        // Scope the PayPal-Request-Id to this run+order+key so the caller's key is unique at
        // PayPal (two orders may legitimately reuse the same caller key), while the caller-key
        // idempotency itself is enforced above against this order's own refunds.
        var payPalRequestId = $"eshop-refund-{RunId}-{orderId}-{idempotencyKey}";
        var result = await _payPal.RefundCaptureAsync(
            payment.CaptureId!, amount, payment.CurrencyCode, payPalRequestId, cancellationToken);

        var refund = new PaymentRefund(result.RefundId, idempotencyKey, result.Amount, result.Status);
        payment.AddRefund(refund);
        await _orderRepository.UpdateAsync(order, cancellationToken);
        _logger.LogInformation("Refunded {0} {1} on order {2} (refund {3}); status now {4}",
            result.Amount, payment.CurrencyCode, orderId, result.RefundId, payment.Status);
        return refund;
    }

    public async Task<IReadOnlyList<Order>> GetOrdersForBuyerAsync(
        string buyerId, CancellationToken cancellationToken = default)
    {
        var orders = await _orderRepository.ListAsync(
            new CustomerOrdersWithPaymentSpecification(buyerId), cancellationToken);
        return orders;
    }

    // --- helpers ---

    private async Task<Order> LoadOrderAsync(int orderId, CancellationToken cancellationToken)
        => await _orderRepository.FirstOrDefaultAsync(new OrderWithPaymentByIdSpecification(orderId), cancellationToken)
           ?? throw new OrderNotFoundException(orderId);

    private async Task<Order> LoadOwnedOrderAsync(string buyerId, int orderId, CancellationToken cancellationToken)
    {
        var order = await LoadOrderAsync(orderId, cancellationToken);
        if (!string.Equals(order.BuyerId, buyerId, StringComparison.Ordinal))
        {
            // Do not reveal that the order exists for another buyer.
            throw new OrderNotFoundException(orderId);
        }
        return order;
    }

    private static void ValidateInstrument(PaymentInstrument instrument)
    {
        var hasCard = instrument.Card is not null;
        var hasSaved = instrument.SavedPaymentMethodId is not null;
        if (hasCard == hasSaved)
        {
            throw new ArgumentException("Provide exactly one of card details or a saved payment method id.");
        }
    }

    private async Task<string> EnsureFreshAuthorizationAsync(Order order, CancellationToken cancellationToken)
    {
        var payment = order.Payment!;
        // If we already know the hold has passed its expiry, renew proactively.
        if (payment.AuthorizationExpiresAt is DateTimeOffset expiry && expiry <= DateTimeOffset.UtcNow)
        {
            return await RenewAuthorizationOrThrowAsync(order, cancellationToken);
        }
        return payment.AuthorizationId;
    }

    private async Task<string> RenewAuthorizationOrThrowAsync(Order order, CancellationToken cancellationToken)
    {
        var payment = order.Payment!;
        var reauthRequestId = $"eshop-reauth-{RunId}-{order.Id}";
        try
        {
            var renewed = await _payPal.ReauthorizeAsync(
                payment.AuthorizationId, payment.Amount, payment.CurrencyCode, reauthRequestId, cancellationToken);
            payment.RenewAuthorization(renewed.AuthorizationId, renewed.Status, renewed.ExpiresAt);
            await _orderRepository.UpdateAsync(order, cancellationToken);
            return renewed.AuthorizationId;
        }
        catch (PayPalApiException ex)
        {
            throw new PaymentStateException(
                $"The authorization for order {order.Id} has expired and can no longer be renewed " +
                $"(PayPal: {ex.Issue ?? ex.Name ?? "unavailable"}). Collect a new payment for this order.");
        }
    }

    private static bool IsExpiredAuthorization(PayPalApiException ex)
    {
        // PayPal reports a lapsed hold as an "…EXPIRED" issue (e.g. AUTHORIZATION_EXPIRED)
        // on a 422 Unprocessable Entity.
        var issue = ex.Issue ?? string.Empty;
        return issue.Contains("EXPIRED", StringComparison.OrdinalIgnoreCase);
    }

    private static string DescribeCard(string brand, string last4)
        => $"{brand} ending {last4}";

    private static string Last4Of(string cardNumber)
    {
        var digits = new string((cardNumber ?? string.Empty).Where(char.IsDigit).ToArray());
        return digits.Length >= 4 ? digits[^4..] : digits;
    }
}
