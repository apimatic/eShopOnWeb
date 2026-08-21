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
using Microsoft.eShopWeb.ApplicationCore.PayPal;
using Microsoft.eShopWeb.ApplicationCore.Specifications;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.ApplicationCore.Services;

/// <summary>
/// Orchestrates the pay-for-an-order flow over the existing order model and the PayPal gateway.
/// State transitions and idempotency live here; the raw PayPal calls live behind
/// <see cref="IPayPalPaymentGateway"/>.
/// </summary>
public class OrderPaymentService : IOrderPaymentService
{
    private const string DefaultPlaceholderPicture = "eCatalog-item-default.png";

    private readonly IRepository<Order> _orderRepository;
    private readonly IRepository<OrderPayment> _paymentRepository;
    private readonly IRepository<CatalogItem> _itemRepository;
    private readonly IRepository<SavedPaymentMethod> _savedCardRepository;
    private readonly IPayPalPaymentGateway _gateway;
    private readonly IUriComposer _uriComposer;
    private readonly PayPalSettings _settings;

    public OrderPaymentService(
        IRepository<Order> orderRepository,
        IRepository<OrderPayment> paymentRepository,
        IRepository<CatalogItem> itemRepository,
        IRepository<SavedPaymentMethod> savedCardRepository,
        IPayPalPaymentGateway gateway,
        IUriComposer uriComposer,
        IOptions<PayPalSettings> settings)
    {
        _orderRepository = orderRepository;
        _paymentRepository = paymentRepository;
        _itemRepository = itemRepository;
        _savedCardRepository = savedCardRepository;
        _gateway = gateway;
        _uriComposer = uriComposer;
        _settings = settings.Value;
    }

    public async Task<int> PlaceOrderAsync(PlaceOrderCommand command, string buyerId, CancellationToken cancellationToken = default)
    {
        Guard.Against.Null(command, nameof(command));
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));

        if (command.Items is null || command.Items.Count == 0)
        {
            throw new InvalidPaymentOperationException("An order must contain at least one item.");
        }

        foreach (var line in command.Items)
        {
            if (line.Quantity <= 0)
            {
                throw new InvalidPaymentOperationException($"Quantity for catalog item {line.CatalogItemId} must be greater than zero.");
            }
        }

        var catalogItemIds = command.Items.Select(i => i.CatalogItemId).Distinct().ToArray();
        var catalogItems = await _itemRepository.ListAsync(new CatalogItemsSpecification(catalogItemIds), cancellationToken);

        var orderItems = new List<OrderItem>();
        foreach (var line in command.Items)
        {
            var catalogItem = catalogItems.FirstOrDefault(c => c.Id == line.CatalogItemId)
                ?? throw new InvalidPaymentOperationException($"Catalog item {line.CatalogItemId} does not exist.");

            var pictureUri = _uriComposer.ComposePicUri(catalogItem.PictureUri);
            if (string.IsNullOrEmpty(pictureUri))
            {
                pictureUri = DefaultPlaceholderPicture;
            }

            var itemOrdered = new CatalogItemOrdered(catalogItem.Id, catalogItem.Name, pictureUri);
            orderItems.Add(new OrderItem(itemOrdered, catalogItem.Price, line.Quantity));
        }

        var address = command.ShipToAddress is { } a
            ? new Address(a.Street, a.City, a.State, a.Country, a.ZipCode)
            : new Address("N/A", "N/A", "N/A", "N/A", "00000");

        var order = new Order(buyerId, address, orderItems);
        order = await _orderRepository.AddAsync(order, cancellationToken);

        var payment = new OrderPayment(order.Id, buyerId, order.Total(), _settings.Currency);
        await _paymentRepository.AddAsync(payment, cancellationToken);

        return order.Id;
    }

    public async Task<OrderPayment> PayAsync(int orderId, PaymentInstrument instrument, string buyerId, CancellationToken cancellationToken = default)
    {
        Guard.Against.Null(instrument, nameof(instrument));
        var (_, payment) = await LoadOwnedOrderAndPaymentAsync(orderId, buyerId, cancellationToken);

        // Idempotent in effect: a repeat once the hold exists returns the existing hold.
        if (payment.Status == PaymentStatus.Authorized)
        {
            return payment;
        }

        if (payment.Status is PaymentStatus.Captured or PaymentStatus.Voided
            or PaymentStatus.Refunded or PaymentStatus.PartiallyRefunded)
        {
            throw new InvalidPaymentOperationException($"Order {orderId} can no longer be paid because it is already {payment.Status}.");
        }

        var (card, vaultId) = await ResolveInstrumentAsync(instrument, buyerId, cancellationToken);

        var request = new PayPalAuthorizationRequest
        {
            Amount = payment.Amount,
            CurrencyCode = payment.CurrencyCode,
            OrderReference = orderId,
            InvoiceReference = payment.InvoiceReference,
            // Derive the idempotency key from the per-payment InvoiceReference (unique across runs,
            // stable within a run) so a double-click is deduped by PayPal while distinct payments —
            // including ones that reuse an OrderId after an in-memory restart — never collide.
            IdempotencyKey = $"eshop-authorize-{payment.InvoiceReference}",
            Card = card,
            VaultId = vaultId
        };

        var result = await _gateway.AuthorizeAsync(request, cancellationToken);

        if (result.RequiresAction)
        {
            payment.MarkActionRequired(result.PayPalOrderId,
                "The card issuer requires the shopper to approve this payment in a browser (e.g. 3-D Secure). Authorization was not completed.");
            await _paymentRepository.UpdateAsync(payment, cancellationToken);
            throw new PaymentActionRequiredException(payment.LastErrorMessage!);
        }

        if (string.IsNullOrEmpty(result.AuthorizationId))
        {
            payment.MarkFailed("PayPal did not return an authorization for the card payment.");
            await _paymentRepository.UpdateAsync(payment, cancellationToken);
            throw new PaymentGatewayException("The card payment could not be authorized.");
        }

        payment.MarkAuthorized(result.PayPalOrderId, result.AuthorizationId, result.AuthorizationStatus, result.ExpiresAt);
        await _paymentRepository.UpdateAsync(payment, cancellationToken);
        return payment;
    }

    public async Task<OrderPayment> FulfilAsync(int orderId, CancellationToken cancellationToken = default)
    {
        var payment = await LoadPaymentAsync(orderId, cancellationToken);

        if (payment.Status == PaymentStatus.Captured)
        {
            return payment; // idempotent: already fulfilled
        }

        if (payment.Status != PaymentStatus.Authorized || string.IsNullOrEmpty(payment.AuthorizationId))
        {
            throw new InvalidPaymentOperationException($"Order {orderId} cannot be fulfilled because it is {payment.Status}, not awaiting fulfilment.");
        }

        // Renew a stale hold rather than failing the fulfilment outright.
        if (IsAuthorizationStale(payment))
        {
            await RenewAuthorizationAsync(payment, cancellationToken);
        }

        PayPalCaptureResult capture;
        try
        {
            capture = await _gateway.CaptureAsync(payment.AuthorizationId!, $"eshop-capture-{payment.AuthorizationId}", cancellationToken);
        }
        catch (PaymentGatewayException) when (!IsAuthorizationStale(payment))
        {
            // PayPal may consider the hold stale even if our expiry says otherwise: renew once and retry.
            await RenewAuthorizationAsync(payment, cancellationToken);
            capture = await _gateway.CaptureAsync(payment.AuthorizationId!, $"eshop-capture-{payment.AuthorizationId}", cancellationToken);
        }

        payment.MarkCaptured(capture.CaptureId, capture.Status, capture.GrossAmount, capture.PayPalFee, capture.NetAmount);
        await _paymentRepository.UpdateAsync(payment, cancellationToken);
        return payment;
    }

    public async Task<OrderPayment> CancelAsync(int orderId, CancellationToken cancellationToken = default)
    {
        var payment = await LoadPaymentAsync(orderId, cancellationToken);

        if (payment.Status == PaymentStatus.Voided)
        {
            return payment; // idempotent
        }

        if (payment.Status is PaymentStatus.Captured or PaymentStatus.Refunded or PaymentStatus.PartiallyRefunded)
        {
            throw new InvalidPaymentOperationException($"Order {orderId} has been fulfilled and cannot be cancelled; issue a refund instead.");
        }

        // Release the held funds if a hold exists; otherwise just record the cancellation.
        if (payment.Status == PaymentStatus.Authorized && !string.IsNullOrEmpty(payment.AuthorizationId))
        {
            await _gateway.VoidAsync(payment.AuthorizationId!, $"eshop-void-{payment.AuthorizationId}", cancellationToken);
        }

        payment.MarkVoided();
        await _paymentRepository.UpdateAsync(payment, cancellationToken);
        return payment;
    }

    public async Task<RefundResult> RefundAsync(int orderId, decimal? amount, string idempotencyKey, string buyerId, CancellationToken cancellationToken = default)
    {
        Guard.Against.NullOrEmpty(idempotencyKey, nameof(idempotencyKey));
        var (_, payment) = await LoadOwnedOrderAndPaymentAsync(orderId, buyerId, cancellationToken);

        if (payment.Status is not (PaymentStatus.Captured or PaymentStatus.PartiallyRefunded) || string.IsNullOrEmpty(payment.CaptureId))
        {
            throw new InvalidPaymentOperationException($"Order {orderId} cannot be refunded because it has not been fulfilled.");
        }

        // Idempotency: the same caller-supplied key must never refund twice.
        var existing = payment.FindRefundByIdempotencyKey(idempotencyKey);
        if (existing is not null)
        {
            return new RefundResult(existing.Id, existing.PayPalRefundId ?? string.Empty, existing.Status, existing.Amount, existing.CurrencyCode, payment);
        }

        var refundAmount = amount ?? payment.RefundableRemaining();
        if (refundAmount <= 0m)
        {
            throw new InvalidPaymentOperationException("The refund amount must be greater than zero.");
        }

        if (refundAmount > payment.RefundableRemaining())
        {
            throw new InvalidPaymentOperationException(
                $"Refund of {refundAmount:0.00} {payment.CurrencyCode} exceeds the refundable remaining amount of {payment.RefundableRemaining():0.00} {payment.CurrencyCode}.");
        }

        var result = await _gateway.RefundAsync(payment.CaptureId!, refundAmount, payment.CurrencyCode, idempotencyKey, cancellationToken);

        var refund = payment.AddRefund(idempotencyKey, refundAmount, result.RefundId, result.Status);
        await _paymentRepository.UpdateAsync(payment, cancellationToken);

        return new RefundResult(refund.Id, result.RefundId, result.Status, refundAmount, payment.CurrencyCode, payment);
    }

    public async Task<IReadOnlyList<MyOrderResult>> GetMyOrdersAsync(string buyerId, CancellationToken cancellationToken = default)
    {
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));

        var orders = await _orderRepository.ListAsync(new CustomerOrdersSpecification(buyerId), cancellationToken);
        var payments = await _paymentRepository.ListAsync(new OrderPaymentsByBuyerSpecification(buyerId), cancellationToken);
        var paymentsByOrder = payments.ToDictionary(p => p.OrderId);

        return orders
            .OrderByDescending(o => o.OrderDate)
            .Select(o => new MyOrderResult(o, paymentsByOrder.GetValueOrDefault(o.Id)))
            .ToList();
    }

    // --- helpers ---

    private bool IsAuthorizationStale(OrderPayment payment) =>
        payment.AuthorizationExpiresAt.HasValue && payment.AuthorizationExpiresAt.Value <= DateTimeOffset.Now;

    private async Task RenewAuthorizationAsync(OrderPayment payment, CancellationToken cancellationToken)
    {
        var renewed = await _gateway.ReauthorizeAsync(
            payment.AuthorizationId!, payment.Amount, payment.CurrencyCode, $"eshop-reauth-{payment.AuthorizationId}", cancellationToken);

        if (string.IsNullOrEmpty(renewed.AuthorizationId))
        {
            throw new AuthorizationRenewalException("The authorization could not be renewed and the order cannot be fulfilled.");
        }

        payment.RenewAuthorization(renewed.AuthorizationId, renewed.AuthorizationStatus, renewed.ExpiresAt);
        await _paymentRepository.UpdateAsync(payment, cancellationToken);
    }

    private async Task<(PayPalCardDetails? Card, string? VaultId)> ResolveInstrumentAsync(
        PaymentInstrument instrument, string buyerId, CancellationToken cancellationToken)
    {
        if (instrument.SavedPaymentMethodId is { } savedId)
        {
            var saved = await _savedCardRepository.FirstOrDefaultAsync(
                new SavedPaymentMethodByIdSpecification(savedId, buyerId), cancellationToken)
                ?? throw new InvalidPaymentOperationException("The saved card was not found for this shopper.");
            return (null, saved.VaultId);
        }

        if (instrument.Card is { } card)
        {
            return (card, null);
        }

        throw new InvalidPaymentOperationException("A card or a saved payment method must be supplied to pay.");
    }

    private async Task<OrderPayment> LoadPaymentAsync(int orderId, CancellationToken cancellationToken)
    {
        return await _paymentRepository.FirstOrDefaultAsync(new OrderPaymentByOrderIdSpecification(orderId), cancellationToken)
            ?? throw new PaymentEntityNotFoundException($"No payment exists for order {orderId}.");
    }

    private async Task<(Order Order, OrderPayment Payment)> LoadOwnedOrderAndPaymentAsync(int orderId, string buyerId, CancellationToken cancellationToken)
    {
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));

        var order = await _orderRepository.GetByIdAsync(orderId, cancellationToken);
        if (order is null || order.BuyerId != buyerId)
        {
            // Do not reveal whether the order exists but belongs to someone else.
            throw new PaymentEntityNotFoundException($"Order {orderId} was not found.");
        }

        var payment = await LoadPaymentAsync(orderId, cancellationToken);
        return (order, payment);
    }
}
