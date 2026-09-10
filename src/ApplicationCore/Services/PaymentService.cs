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
using Microsoft.eShopWeb.ApplicationCore.Interfaces.PayPal;
using Microsoft.eShopWeb.ApplicationCore.Specifications;

namespace Microsoft.eShopWeb.ApplicationCore.Services;

/// <summary>
/// Orchestrates the payment lifecycle over PayPal. Every money-moving step is idempotent in effect:
/// re-issuing the same logical operation (a double-click) neither authorizes nor captures twice,
/// because the eShop-side state is checked first and a stable PayPal-Request-Id lets PayPal dedupe.
/// </summary>
public class PaymentService : IPaymentService
{
    private readonly IRepository<Order> _orderRepository;
    private readonly IRepository<Payment> _paymentRepository;
    private readonly IReadRepository<CatalogItem> _catalogRepository;
    private readonly IReadRepository<SavedCard> _savedCardRepository;
    private readonly IUriComposer _uriComposer;
    private readonly IPayPalGateway _payPal;
    private readonly PayPalSettings _settings;
    private readonly IAppLogger<PaymentService> _logger;

    public PaymentService(
        IRepository<Order> orderRepository,
        IRepository<Payment> paymentRepository,
        IReadRepository<CatalogItem> catalogRepository,
        IReadRepository<SavedCard> savedCardRepository,
        IUriComposer uriComposer,
        IPayPalGateway payPal,
        PayPalSettings settings,
        IAppLogger<PaymentService> logger)
    {
        _orderRepository = orderRepository;
        _paymentRepository = paymentRepository;
        _catalogRepository = catalogRepository;
        _savedCardRepository = savedCardRepository;
        _uriComposer = uriComposer;
        _payPal = payPal;
        _settings = settings;
        _logger = logger;
    }

    private string Currency => _settings.Currency;

    public async Task<int> PlaceOrderAsync(
        string buyerId, IReadOnlyCollection<PlaceOrderItem> items, ShippingAddressInfo? shipTo,
        CancellationToken cancellationToken = default)
    {
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));
        Guard.Against.Null(items, nameof(items));
        if (items.Count == 0)
        {
            throw new PaymentDomainException("An order must contain at least one item.");
        }

        // Prices come from the catalog, not the caller.
        var ids = items.Select(i => i.CatalogItemId).Distinct().ToArray();
        var catalogItems = await _catalogRepository.ListAsync(new CatalogItemsSpecification(ids), cancellationToken);

        var orderItems = new List<OrderItem>();
        foreach (var line in items)
        {
            if (line.Quantity <= 0)
            {
                throw new PaymentDomainException($"Quantity for catalog item {line.CatalogItemId} must be greater than zero.");
            }

            var catalogItem = catalogItems.FirstOrDefault(c => c.Id == line.CatalogItemId)
                ?? throw new EntityNotFoundException($"Catalog item {line.CatalogItemId} was not found.");

            var itemOrdered = new CatalogItemOrdered(
                catalogItem.Id, catalogItem.Name, _uriComposer.ComposePicUri(catalogItem.PictureUri));
            orderItems.Add(new OrderItem(itemOrdered, catalogItem.Price, line.Quantity));
        }

        var address = shipTo is null
            ? new Address("N/A", "N/A", "N/A", "N/A", "N/A")
            : new Address(shipTo.Street, shipTo.City, shipTo.State, shipTo.Country, shipTo.ZipCode);

        var order = new Order(buyerId, address, orderItems);
        await _orderRepository.AddAsync(order, cancellationToken);

        _logger.LogInformation($"Order {order.Id} placed for buyer, awaiting payment. Total {order.Total():0.00} {Currency}.");
        return order.Id;
    }

    public async Task<Payment> PayAsync(
        string buyerId, int orderId, PayRequestCommand command, CancellationToken cancellationToken = default)
    {
        var order = await LoadOwnedOrderAsync(buyerId, orderId, cancellationToken);
        var payment = await _paymentRepository.FirstOrDefaultAsync(
            new PaymentByOrderIdSpecification(orderId), cancellationToken);

        // Idempotency: if the hold (or capture) already exists, do not authorize again.
        if (payment is not null && payment.Status is PaymentStatus.Authorized or PaymentStatus.Captured
            or PaymentStatus.PartiallyRefunded or PaymentStatus.Refunded)
        {
            _logger.LogInformation($"Order {orderId} is already '{payment.Status}'; returning existing payment (idempotent).");
            return payment;
        }

        var (source, savedCardId) = await ResolvePaymentSourceAsync(buyerId, command, cancellationToken);

        var amount = order.Total();
        if (amount <= 0m)
        {
            throw new PaymentDomainException($"Order {orderId} has a non-positive total and cannot be paid.");
        }

        var isNew = payment is null;
        payment ??= new Payment(orderId, buyerId, Currency, amount);
        payment.SetSavedCard(savedCardId);

        // Create the PayPal order once (holds the amount to the cent), reusing it on retry.
        if (payment.PayPalOrderId is null)
        {
            var invoiceId = BuildInvoiceId(orderId);
            var createCommand = new CreateOrderCommand(
                Amount: amount,
                CurrencyCode: Currency,
                InvoiceId: invoiceId,
                CustomId: orderId.ToString(CultureInfo.InvariantCulture),
                Description: $"eShopOnWeb order {orderId}");

            var ppOrder = await _payPal.CreateAuthorizationOrderAsync(
                createCommand, idempotencyKey: payment.OrderKey, cancellationToken);
            payment.SetPayPalOrder(ppOrder.Id, invoiceId);

            // Persist the PayPal order id before authorizing, so a retry reuses it.
            if (isNew)
            {
                payment = await _paymentRepository.AddAsync(payment, cancellationToken);
                isNew = false;
            }
            else
            {
                await _paymentRepository.UpdateAsync(payment, cancellationToken);
            }
        }

        var authorization = await _payPal.AuthorizeOrderAsync(
            payment.PayPalOrderId!, source, idempotencyKey: payment.AuthorizeKey, cancellationToken);

        payment.MarkAuthorized(authorization.Id, authorization.Status, authorization.ExpiresAt);
        order.SetStatus(OrderStatus.PaymentAuthorized);

        if (isNew)
        {
            payment = await _paymentRepository.AddAsync(payment, cancellationToken);
        }
        else
        {
            await _paymentRepository.UpdateAsync(payment, cancellationToken);
        }
        await _orderRepository.UpdateAsync(order, cancellationToken);

        _logger.LogInformation($"Order {orderId} authorized. PayPal authorization {authorization.Id} ({authorization.Status}).");
        return payment;
    }

    public async Task<Payment> FulfilAsync(int orderId, CancellationToken cancellationToken = default)
    {
        var payment = await LoadPaymentAsync(orderId, cancellationToken);
        var order = await _orderRepository.GetByIdAsync(orderId, cancellationToken);

        if (payment.Status is PaymentStatus.Captured or PaymentStatus.PartiallyRefunded or PaymentStatus.Refunded)
        {
            _logger.LogInformation($"Order {orderId} is already captured; returning existing payment (idempotent).");
            return payment;
        }

        if (payment.Status != PaymentStatus.Authorized || payment.AuthorizationId is null)
        {
            throw new PaymentDomainException(
                $"Order {orderId} cannot be fulfilled because its payment is '{payment.Status}', not an active hold.");
        }

        // Renew a hold that has already lapsed before attempting the capture.
        if (payment.AuthorizationExpiresAt is { } expiry && expiry <= DateTimeOffset.UtcNow)
        {
            await RenewHoldAsync(payment, orderId, cancellationToken);
        }

        PayPalCaptureResult capture;
        try
        {
            capture = await _payPal.CaptureAuthorizationAsync(
                payment.AuthorizationId!, idempotencyKey: payment.CaptureKey, cancellationToken);
        }
        catch (PayPalGatewayException ex) when (IsExpiredAuthorization(ex))
        {
            // The hold went stale between authorization and fulfilment: renew it, then capture the new hold.
            _logger.LogWarning($"Authorization for order {orderId} was stale on capture ({ex.Issue}); renewing.");
            await RenewHoldAsync(payment, orderId, cancellationToken);
            capture = await _payPal.CaptureAuthorizationAsync(
                payment.AuthorizationId!, idempotencyKey: payment.CaptureRenewedKey, cancellationToken);
        }

        payment.MarkCaptured(capture.Id, capture.Status, capture.GrossAmount, capture.PayPalFee, capture.NetAmount);
        order?.SetStatus(OrderStatus.Fulfilled);

        await _paymentRepository.UpdateAsync(payment, cancellationToken);
        if (order is not null)
        {
            await _orderRepository.UpdateAsync(order, cancellationToken);
        }

        _logger.LogInformation(
            $"Order {orderId} fulfilled. Captured {capture.GrossAmount:0.00} {Currency}, fee {capture.PayPalFee:0.00}, net {capture.NetAmount:0.00}.");
        return payment;
    }

    public async Task<Payment> CancelAsync(int orderId, CancellationToken cancellationToken = default)
    {
        var payment = await LoadPaymentAsync(orderId, cancellationToken);
        var order = await _orderRepository.GetByIdAsync(orderId, cancellationToken);

        if (payment.Status == PaymentStatus.Voided)
        {
            _logger.LogInformation($"Order {orderId} hold is already released; returning existing payment (idempotent).");
            return payment;
        }

        if (payment.Status != PaymentStatus.Authorized || payment.AuthorizationId is null)
        {
            throw new PaymentDomainException(
                $"Order {orderId} cannot be cancelled because its payment is '{payment.Status}'. " +
                "Cancel only releases a hold that has not yet been captured; use a refund after fulfilment.");
        }

        await _payPal.VoidAuthorizationAsync(payment.AuthorizationId!, cancellationToken);

        payment.MarkVoided();
        order?.SetStatus(OrderStatus.Cancelled);

        await _paymentRepository.UpdateAsync(payment, cancellationToken);
        if (order is not null)
        {
            await _orderRepository.UpdateAsync(order, cancellationToken);
        }

        _logger.LogInformation($"Order {orderId} cancelled; hold {payment.AuthorizationId} released.");
        return payment;
    }

    public async Task<(Payment Payment, Refund Refund)> RefundAsync(
        string buyerId, int orderId, decimal? amount, string idempotencyKey, CancellationToken cancellationToken = default)
    {
        Guard.Against.NullOrEmpty(idempotencyKey, nameof(idempotencyKey));

        var order = await LoadOwnedOrderAsync(buyerId, orderId, cancellationToken);
        var payment = await _paymentRepository.FirstOrDefaultAsync(
            new PaymentByOrderIdSpecification(orderId), cancellationToken)
            ?? throw new EntityNotFoundException($"No payment exists for order {orderId}.");

        // Idempotency: a repeat of the same key returns the original refund, never a second one.
        var existing = payment.FindRefundByIdempotencyKey(idempotencyKey);
        if (existing is not null)
        {
            _logger.LogInformation($"Refund for order {orderId} with key '{idempotencyKey}' already exists; returning it (idempotent).");
            return (payment, existing);
        }

        if (payment.CaptureId is null || payment.Status is not (PaymentStatus.Captured or PaymentStatus.PartiallyRefunded))
        {
            throw new PaymentDomainException(
                $"Order {orderId} cannot be refunded because its payment is '{payment.Status}', not captured.");
        }

        if (amount is not null && amount.Value <= 0m)
        {
            throw new PaymentDomainException("Refund amount must be greater than zero.");
        }

        var refundable = payment.RefundableAmount();
        var requested = amount ?? refundable;
        if (requested > refundable)
        {
            throw new PaymentDomainException(
                $"Refund of {requested:0.00} exceeds the refundable amount {refundable:0.00} for order {orderId}.");
        }

        var result = await _payPal.RefundCaptureAsync(
            payment.CaptureId!, amount, Currency, idempotencyKey, cancellationToken);

        // PayPal's reported amount is authoritative; the domain re-checks the running total.
        var refund = payment.AddRefund(result.Id, result.Amount, result.Status, idempotencyKey);

        order.SetStatus(payment.Status == PaymentStatus.Refunded ? OrderStatus.Refunded : OrderStatus.PartiallyRefunded);

        await _paymentRepository.UpdateAsync(payment, cancellationToken);
        await _orderRepository.UpdateAsync(order, cancellationToken);

        _logger.LogInformation($"Order {orderId} refunded {result.Amount:0.00} {Currency}. PayPal refund {result.Id} ({result.Status}).");
        return (payment, refund);
    }

    public async Task<IReadOnlyList<(Order Order, Payment? Payment)>> GetMyOrdersAsync(
        string buyerId, CancellationToken cancellationToken = default)
    {
        var orders = await _orderRepository.ListAsync(new CustomerOrdersWithItemsSpecification(buyerId), cancellationToken);
        var payments = await _paymentRepository.ListAsync(new PaymentsByBuyerSpecification(buyerId), cancellationToken);
        var paymentsByOrder = payments.ToDictionary(p => p.OrderId);

        return orders
            .Select(o => (o, paymentsByOrder.TryGetValue(o.Id, out var p) ? p : null))
            .OrderByDescending(t => t.o.Id)
            .ToList();
    }

    // ----- helpers -----

    private async Task<Order> LoadOwnedOrderAsync(string buyerId, int orderId, CancellationToken cancellationToken)
    {
        var order = await _orderRepository.FirstOrDefaultAsync(new OrderWithItemsByIdSpec(orderId), cancellationToken);
        if (order is null || order.BuyerId != buyerId)
        {
            // Do not distinguish "not found" from "not yours": one shopper cannot probe another's orders.
            throw new EntityNotFoundException($"Order {orderId} was not found.");
        }
        return order;
    }

    private async Task<Payment> LoadPaymentAsync(int orderId, CancellationToken cancellationToken)
    {
        return await _paymentRepository.FirstOrDefaultAsync(new PaymentByOrderIdSpecification(orderId), cancellationToken)
            ?? throw new EntityNotFoundException($"No payment exists for order {orderId}.");
    }

    private async Task<(PayPalCardPaymentSource Source, int? SavedCardId)> ResolvePaymentSourceAsync(
        string buyerId, PayRequestCommand command, CancellationToken cancellationToken)
    {
        if (command.SavedCardId is { } savedCardId)
        {
            var card = await _savedCardRepository.FirstOrDefaultAsync(
                new SavedCardByIdForBuyerSpecification(savedCardId, buyerId), cancellationToken)
                ?? throw new EntityNotFoundException($"Saved card {savedCardId} was not found.");

            return (new PayPalCardPaymentSource(null, card.PayPalVaultId), savedCardId);
        }

        if (command.Card is { } c)
        {
            var raw = new PayPalRawCard(
                Number: c.Number,
                Expiry: c.Expiry,
                SecurityCode: c.SecurityCode,
                Name: c.CardholderName,
                BillingAddress: BuildBillingAddress(c));
            return (new PayPalCardPaymentSource(raw, null), null);
        }

        throw new PaymentDomainException("A payment must supply either card details or a saved card id.");
    }

    private static PayPalBillingAddress? BuildBillingAddress(CardDetails c)
    {
        if (string.IsNullOrWhiteSpace(c.CountryCode) && string.IsNullOrWhiteSpace(c.AddressLine1)
            && string.IsNullOrWhiteSpace(c.PostalCode))
        {
            return null;
        }

        return new PayPalBillingAddress(
            AddressLine1: c.AddressLine1,
            AddressLine2: c.AddressLine2,
            AdminArea2: c.City,
            AdminArea1: c.State,
            PostalCode: c.PostalCode,
            CountryCode: string.IsNullOrWhiteSpace(c.CountryCode) ? "US" : c.CountryCode!);
    }

    private async Task RenewHoldAsync(Payment payment, int orderId, CancellationToken cancellationToken)
    {
        try
        {
            var renewed = await _payPal.ReauthorizeAsync(
                payment.AuthorizationId!, payment.Amount, Currency, idempotencyKey: payment.ReauthorizeKey, cancellationToken);
            payment.RenewAuthorization(renewed.Id, renewed.Status, renewed.ExpiresAt);
            await _paymentRepository.UpdateAsync(payment, cancellationToken);
            _logger.LogInformation($"Order {orderId} hold renewed. New authorization {renewed.Id} ({renewed.Status}).");
        }
        catch (PayPalGatewayException ex)
        {
            throw new AuthorizationRenewalException(
                $"The funds hold for order {orderId} has lapsed and can no longer be renewed by PayPal " +
                $"({ex.Issue ?? ex.Message}). Ask the shopper to pay again before fulfilling this order.", ex);
        }
    }

    private static bool IsExpiredAuthorization(PayPalGatewayException ex)
    {
        var issue = ex.Issue ?? string.Empty;
        return issue.Contains("EXPIRED", StringComparison.OrdinalIgnoreCase)
            || ex.Message.Contains("AUTHORIZATION_EXPIRED", StringComparison.OrdinalIgnoreCase);
    }

    private static string BuildInvoiceId(int orderId) =>
        $"ESHOP-{orderId}-{DateTime.UtcNow:yyyyMMddHHmmss}-{Guid.NewGuid():N}";
}
