using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentMethodAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Interfaces.PayPal;
using Microsoft.eShopWeb.ApplicationCore.Specifications;

namespace Microsoft.eShopWeb.ApplicationCore.Services;

public class PaymentService : IPaymentService
{
    private readonly IRepository<Order> _orderRepository;
    private readonly IRepository<CatalogItem> _itemRepository;
    private readonly IReadRepository<SavedCard> _savedCardRepository;
    private readonly IPayPalClient _payPalClient;
    private readonly IUriComposer _uriComposer;

    public PaymentService(
        IRepository<Order> orderRepository,
        IRepository<CatalogItem> itemRepository,
        IReadRepository<SavedCard> savedCardRepository,
        IPayPalClient payPalClient,
        IUriComposer uriComposer)
    {
        _orderRepository = orderRepository;
        _itemRepository = itemRepository;
        _savedCardRepository = savedCardRepository;
        _payPalClient = payPalClient;
        _uriComposer = uriComposer;
    }

    public async Task<int> PlaceOrderAsync(string buyerId, IReadOnlyCollection<OrderLineRequest> lines, ShippingAddressRequest? shippingAddress, CancellationToken cancellationToken = default)
    {
        if (lines is null || lines.Count == 0)
            throw new InvalidRequestException("An order must contain at least one item.");
        if (lines.Any(l => l.Quantity <= 0))
            throw new InvalidRequestException("Every order line must have a quantity of at least 1.");

        var catalogItemIds = lines.Select(l => l.CatalogItemId).Distinct().ToArray();
        var catalogItems = await _itemRepository.ListAsync(new CatalogItemsSpecification(catalogItemIds), cancellationToken);

        var missing = catalogItemIds.Where(id => catalogItems.All(c => c.Id != id)).ToArray();
        if (missing.Length > 0)
            throw new InvalidRequestException($"Unknown catalog item id(s): {string.Join(", ", missing)}.");

        // Amounts come from catalog prices, snapshotted onto the order items.
        var orderItems = lines.Select(line =>
        {
            var catalogItem = catalogItems.First(c => c.Id == line.CatalogItemId);
            var itemOrdered = new CatalogItemOrdered(catalogItem.Id, catalogItem.Name, _uriComposer.ComposePicUri(catalogItem.PictureUri));
            return new OrderItem(itemOrdered, catalogItem.Price, line.Quantity);
        }).ToList();

        var address = shippingAddress is null
            ? new Address("N/A", "N/A", "N/A", "US", "00000")
            : new Address(shippingAddress.Street, shippingAddress.City, shippingAddress.State, shippingAddress.Country, shippingAddress.ZipCode);

        var order = new Order(buyerId, address, orderItems);
        order = await _orderRepository.AddAsync(order, cancellationToken);
        return order.Id;
    }

    public async Task<Order> PayOrderAsync(string buyerId, int orderId, PaymentInstruction instruction, CancellationToken cancellationToken = default)
    {
        var order = await LoadOwnedOrderAsync(buyerId, orderId, cancellationToken);

        switch (order.State)
        {
            case OrderState.PaymentAuthorized:
                // Idempotent: the hold already exists. A double-click never authorizes twice.
                return order;
            case OrderState.Fulfilled:
                throw new ConflictException("This order has already been fulfilled and cannot be paid again.");
            case OrderState.Cancelled:
                throw new ConflictException("This order has been cancelled and cannot be paid.");
        }

        var (card, vaultId, cardDescription, sourceKey) = await ResolvePaymentSourceAsync(buyerId, instruction, cancellationToken);

        var amount = order.Total();
        if (amount <= 0m)
            throw new InvalidRequestException("The order total must be greater than zero.");

        // A stable invoice id per successful authorization, and reconcilable in transaction reports.
        var invoiceId = $"ESHOP-{orderId}-{ShortToken()}";
        // Keyed on the order's unique reference (not the resettable int id) so it is stable across
        // double-clicks yet never collides with another order or a previous run.
        var idempotencyKey = $"auth-{order.PaymentReference}-{sourceKey}";

        var result = await _payPalClient.CreateAuthorizedOrderAsync(
            new AuthorizeOrderRequest(amount, invoiceId, $"ESHOP-{orderId}", card, vaultId),
            idempotencyKey, cancellationToken);

        if (result.RequiresApproval)
            throw new PaymentChallengeRequiredException(
                "PayPal requires the shopper to approve this card payment in a browser (3-D Secure challenge). " +
                "This integration processes cards without a browser step, so the payment was stopped. Try a card that does not require a challenge.");

        if (string.IsNullOrEmpty(result.AuthorizationId))
            throw new PaymentException($"The card payment could not be authorized (PayPal order status: {result.OrderStatus}).");

        var description = BuildCardDescription(result.CardBrand, result.CardLast4) ?? cardDescription;

        var payment = new OrderPayment(
            result.PayPalOrderId,
            invoiceId,
            result.AuthorizationId!,
            result.AuthorizationStatus ?? "CREATED",
            result.AuthorizationExpiresAt,
            amount,
            result.Currency,
            description);

        order.AttachAuthorizedPayment(payment);
        await _orderRepository.UpdateAsync(order, cancellationToken);
        return order;
    }

    public async Task<Order> FulfilOrderAsync(int orderId, CancellationToken cancellationToken = default)
    {
        var order = await LoadOrderAsync(orderId, cancellationToken);

        switch (order.State)
        {
            case OrderState.AwaitingPayment:
                throw new ConflictException("This order has not been paid, so it cannot be fulfilled.");
            case OrderState.Cancelled:
                throw new ConflictException("This order has been cancelled and cannot be fulfilled.");
            case OrderState.Fulfilled when order.Payment?.CaptureId is not null:
                return order; // Idempotent: already captured.
        }

        var payment = order.Payment ?? throw new ConflictException("This order has no payment to capture.");

        // Renew a hold that is (or is about to be) stale before attempting to capture.
        if (payment.IsAuthorizationExpired(DateTimeOffset.UtcNow))
        {
            await RenewAuthorizationOrThrowAsync(payment, cancellationToken);
        }

        CaptureResult capture;
        try
        {
            capture = await _payPalClient.CaptureAuthorizationAsync(payment.AuthorizationId, $"capture-{order.PaymentReference}", cancellationToken);
        }
        catch (PayPalApiException ex) when (IndicatesExpiredAuthorization(ex))
        {
            // The hold went stale between our check and the capture — renew once and retry.
            await RenewAuthorizationOrThrowAsync(payment, cancellationToken);
            capture = await _payPalClient.CaptureAuthorizationAsync(payment.AuthorizationId, $"capture-{order.PaymentReference}-r", cancellationToken);
        }

        payment.RecordCapture(capture.CaptureId, capture.Status, capture.GrossAmount, capture.PayPalFee, capture.NetAmount);
        order.MarkFulfilled();
        await _orderRepository.UpdateAsync(order, cancellationToken);
        return order;
    }

    public async Task<Order> CancelOrderAsync(int orderId, CancellationToken cancellationToken = default)
    {
        var order = await LoadOrderAsync(orderId, cancellationToken);

        switch (order.State)
        {
            case OrderState.Cancelled:
                return order; // Idempotent.
            case OrderState.Fulfilled:
                throw new ConflictException("This order has already been fulfilled; issue a refund instead of cancelling.");
        }

        if (order.State == OrderState.PaymentAuthorized && order.Payment is not null)
        {
            // Release the hold so no money ever moved.
            await _payPalClient.VoidAuthorizationAsync(order.Payment.AuthorizationId, cancellationToken);
            order.Payment.MarkAuthorizationVoided();
        }

        order.MarkCancelled();
        await _orderRepository.UpdateAsync(order, cancellationToken);
        return order;
    }

    public async Task<(Order Order, int RefundId)> RefundOrderAsync(string buyerId, int orderId, decimal? amount, string idempotencyKey, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(idempotencyKey))
            throw new InvalidRequestException("A refund requires an idempotency key.");

        var order = await LoadOwnedOrderAsync(buyerId, orderId, cancellationToken);

        var payment = order.Payment;
        if (order.State != OrderState.Fulfilled || payment?.CaptureId is null)
            throw new ConflictException("This order has not been fulfilled/captured, so there is nothing to refund.");

        // Idempotent: the same key never refunds twice.
        var existing = payment.FindRefundByIdempotencyKey(idempotencyKey);
        if (existing is not null)
            return (order, existing.Id);

        var remaining = payment.RefundableRemaining();
        if (remaining <= 0m)
            throw new ConflictException("This capture has already been fully refunded.");

        if (amount.HasValue)
        {
            if (amount.Value <= 0m)
                throw new InvalidRequestException("The refund amount must be greater than zero.");
            if (amount.Value > remaining)
                throw new InvalidRequestException($"The refund amount {amount.Value} exceeds the refundable remaining {remaining}.");
        }

        // Namespace the caller's key with this order's unique reference for PayPal, so the same key
        // used on a different order (or a previous run) never returns the wrong cached refund. The
        // app-layer dedup above still keys on the caller's raw key, scoped to this payment.
        var payPalRequestId = $"refund-{order.PaymentReference}-{idempotencyKey}";
        var result = await _payPalClient.RefundCaptureAsync(payment.CaptureId!, amount, payPalRequestId, cancellationToken);

        var refund = new OrderRefund(result.RefundId, idempotencyKey, result.Amount, result.Status);
        payment.AddRefund(refund);
        await _orderRepository.UpdateAsync(order, cancellationToken);
        return (order, refund.Id);
    }

    public async Task<IReadOnlyList<Order>> GetMyOrdersAsync(string buyerId, CancellationToken cancellationToken = default)
    {
        var orders = await _orderRepository.ListAsync(new CustomerOrdersWithPaymentSpecification(buyerId), cancellationToken);
        return orders;
    }

    // ----- helpers -----

    private async Task<Order> LoadOrderAsync(int orderId, CancellationToken cancellationToken)
    {
        var order = await _orderRepository.FirstOrDefaultAsync(new OrderWithPaymentByIdSpec(orderId), cancellationToken);
        return order ?? throw new EntityNotFoundException($"No order found with id {orderId}.");
    }

    private async Task<Order> LoadOwnedOrderAsync(string buyerId, int orderId, CancellationToken cancellationToken)
    {
        var order = await LoadOrderAsync(orderId, cancellationToken);
        if (!string.Equals(order.BuyerId, buyerId, StringComparison.Ordinal))
            throw new ForbiddenActionException("This order belongs to another shopper.");
        return order;
    }

    private async Task<(PayPalCard? card, string? vaultId, string cardDescription, string sourceKey)> ResolvePaymentSourceAsync(
        string buyerId, PaymentInstruction instruction, CancellationToken cancellationToken)
    {
        if (instruction.SavedCardId.HasValue)
        {
            var savedCard = await _savedCardRepository.GetByIdAsync(instruction.SavedCardId.Value, cancellationToken);
            if (savedCard is null || !string.Equals(savedCard.BuyerId, buyerId, StringComparison.Ordinal))
                throw new EntityNotFoundException($"No saved card found with id {instruction.SavedCardId.Value}.");

            var description = BuildCardDescription(savedCard.Brand, savedCard.Last4) ?? "Saved card";
            return (null, savedCard.VaultTokenId, description, $"vault-{savedCard.VaultTokenId}");
        }

        if (instruction.Card is not null)
        {
            var card = instruction.Card;
            if (string.IsNullOrWhiteSpace(card.Number) || string.IsNullOrWhiteSpace(card.Expiry) || string.IsNullOrWhiteSpace(card.SecurityCode))
                throw new InvalidRequestException("Card number, expiry and security code are all required.");

            var last4 = card.Number.Length >= 4 ? card.Number[^4..] : card.Number;
            var description = $"Card ending {last4}";
            return (card, null, description, HashSource(card.Number + "|" + card.Expiry));
        }

        throw new InvalidRequestException("Provide either card details or the id of a saved card to pay with.");
    }

    private async Task RenewAuthorizationOrThrowAsync(OrderPayment payment, CancellationToken cancellationToken)
    {
        try
        {
            var reauth = await _payPalClient.ReauthorizeAsync(payment.AuthorizationId, payment.AuthorizedAmount, cancellationToken);
            payment.RenewAuthorization(reauth.AuthorizationId, reauth.Status, reauth.ExpiresAt);
        }
        catch (PayPalApiException)
        {
            throw new PaymentException(
                "The authorization has expired and could not be renewed. Ask the customer to pay the order again so a new hold can be placed, then fulfil it.");
        }
    }

    private static bool IndicatesExpiredAuthorization(PayPalApiException ex)
    {
        if (ex.StatusCode != 422) return false;
        var haystack = string.Join(" ", new[] { ex.Name, ex.Message }.Concat(ex.Details)).ToUpperInvariant();
        return haystack.Contains("EXPIRED") || haystack.Contains("AUTHORIZATION_EXPIRED");
    }

    private static string? BuildCardDescription(string? brand, string? last4)
    {
        if (string.IsNullOrEmpty(last4)) return string.IsNullOrEmpty(brand) ? null : brand;
        return string.IsNullOrEmpty(brand) ? $"Card ending {last4}" : $"{brand} ending {last4}";
    }

    private static string ShortToken() => Guid.NewGuid().ToString("N")[..8];

    private static string HashSource(string value)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(value));
        return Convert.ToHexString(bytes)[..16];
    }
}
