using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Ardalis.GuardClauses;
using Microsoft.eShopWeb.ApplicationCore.Entities;
using Microsoft.eShopWeb.ApplicationCore.Entities.BuyerAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Payments;
using Microsoft.eShopWeb.ApplicationCore.Specifications;

namespace Microsoft.eShopWeb.ApplicationCore.Services;

public class OrderPaymentService : IOrderPaymentService
{
    // Renew a hold if it will expire within this window before capture.
    private static readonly TimeSpan ReauthorizeBuffer = TimeSpan.FromMinutes(5);

    private readonly IRepository<Order> _orderRepository;
    private readonly IReadRepository<CatalogItem> _itemRepository;
    private readonly IRepository<Buyer> _buyerRepository;
    private readonly IPayPalPaymentGateway _gateway;
    private readonly IPaymentSettings _settings;
    private readonly IUriComposer _uriComposer;
    private readonly IAppLogger<OrderPaymentService> _logger;

    public OrderPaymentService(
        IRepository<Order> orderRepository,
        IReadRepository<CatalogItem> itemRepository,
        IRepository<Buyer> buyerRepository,
        IPayPalPaymentGateway gateway,
        IPaymentSettings settings,
        IUriComposer uriComposer,
        IAppLogger<OrderPaymentService> logger)
    {
        _orderRepository = orderRepository;
        _itemRepository = itemRepository;
        _buyerRepository = buyerRepository;
        _gateway = gateway;
        _settings = settings;
        _uriComposer = uriComposer;
        _logger = logger;
    }

    public async Task<Order> PlaceOrderAsync(
        string buyerId, IReadOnlyCollection<OrderLine> lines, ShippingAddressInput? shipTo,
        CancellationToken cancellationToken = default)
    {
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));
        if (lines is null || lines.Count == 0)
        {
            throw new InvalidPaymentRequestException("An order must contain at least one item.");
        }
        if (lines.Any(l => l.Quantity <= 0))
        {
            throw new InvalidPaymentRequestException("Every order line must have a quantity of at least 1.");
        }

        var ids = lines.Select(l => l.CatalogItemId).Distinct().ToArray();
        var catalogItems = await _itemRepository.ListAsync(new CatalogItemsSpecification(ids), cancellationToken);

        var items = new List<OrderItem>();
        foreach (var line in lines)
        {
            var catalogItem = catalogItems.FirstOrDefault(c => c.Id == line.CatalogItemId)
                ?? throw new InvalidPaymentRequestException($"Catalog item {line.CatalogItemId} does not exist.");

            var itemOrdered = new CatalogItemOrdered(
                catalogItem.Id, catalogItem.Name, _uriComposer.ComposePicUri(catalogItem.PictureUri));
            items.Add(new OrderItem(itemOrdered, catalogItem.Price, line.Quantity));
        }

        var order = new Order(buyerId, ToAddress(shipTo), items);
        await _orderRepository.AddAsync(order, cancellationToken);
        _logger.LogInformation("Placed order {OrderId} for buyer with total {Total}.", order.Id, order.Total());
        return order;
    }

    public async Task<Order> PayAsync(
        int orderId, string buyerId, PaymentInstruction instruction, CancellationToken cancellationToken = default)
    {
        var order = await LoadOwnedOrderAsync(orderId, buyerId, cancellationToken);

        // Idempotent: a repeated pay on an already-authorized order returns the current state.
        if (order.Status == OrderStatus.Authorized)
        {
            return order;
        }
        if (order.Status != OrderStatus.AwaitingPayment)
        {
            throw new PaymentConflictException(
                $"Order {orderId} cannot be paid because it is {order.Status}.");
        }

        var (card, vaultId, description) = await ResolveInstrumentAsync(buyerId, instruction, cancellationToken);

        var amount = order.Total();
        var currency = _settings.Currency;

        // The key dedupes double-clicks (same order → same key), yet stays unique across app restarts
        // (which, on the in-memory provider, reuse order ids) by including the placement timestamp.
        var idempotencyKey = $"{orderId}-{order.OrderDate.Ticks}";

        var result = await _gateway.AuthorizeAsync(
            amount, currency, referenceId: $"order-{orderId}",
            card: card, vaultId: vaultId, idempotencyKey: idempotencyKey, cancellationToken);

        var payment = new Payment(
            result.PayPalOrderId, result.AuthorizationId, result.AuthorizationStatus,
            result.ExpiresAt, amount, currency, description);

        order.MarkAuthorized(payment);
        await _orderRepository.UpdateAsync(order, cancellationToken);
        _logger.LogInformation(
            "Authorized order {OrderId}: PayPal order {PayPalOrderId}, authorization {AuthorizationId}.",
            orderId, result.PayPalOrderId, result.AuthorizationId);
        return order;
    }

    public async Task<Order> FulfilAsync(int orderId, CancellationToken cancellationToken = default)
    {
        var order = await LoadAnyOrderAsync(orderId, cancellationToken);

        // Idempotent: capturing an already-fulfilled order returns the current state.
        if (order.Status is OrderStatus.Fulfilled or OrderStatus.PartiallyRefunded or OrderStatus.Refunded)
        {
            return order;
        }
        if (order.Status != OrderStatus.Authorized)
        {
            throw new PaymentConflictException(
                $"Order {orderId} cannot be fulfilled because it is {order.Status}.");
        }

        var payment = order.Payment!;
        var amount = payment.Amount;
        var currency = payment.Currency;
        var reauthorized = false;

        // Renew a hold that has gone (or is about to go) stale before we capture.
        if (payment.AuthorizationExpiresAt is { } expires &&
            expires - ReauthorizeBuffer <= DateTimeOffset.UtcNow)
        {
            await RenewAuthorizationAsync(order, amount, currency, cancellationToken);
            reauthorized = true;
        }

        PayPalCaptureResult capture;
        try
        {
            capture = await CaptureAsync(order.Payment!.AuthorizationId, amount, currency, orderId, cancellationToken);
        }
        catch (PaymentGatewayException ex) when (!reauthorized && ex.ProviderStatusCode is >= 400 and < 500)
        {
            // The hold may have expired between authorization and fulfilment. Renew and retry once;
            // if it can no longer be renewed, RenewAuthorizationAsync surfaces an operator message.
            _logger.LogWarning(
                "Capture of order {OrderId} failed ({Status}); attempting to renew the authorization.",
                orderId, ex.ProviderStatusCode);
            await RenewAuthorizationAsync(order, amount, currency, cancellationToken);
            capture = await CaptureAsync(order.Payment!.AuthorizationId, amount, currency, orderId, cancellationToken);
        }

        order.MarkFulfilled(capture.CaptureId, capture.Status, capture.Gross, capture.Fee, capture.Net);
        await _orderRepository.UpdateAsync(order, cancellationToken);
        _logger.LogInformation(
            "Fulfilled order {OrderId}: captured {Gross} (fee {Fee}, net {Net}).",
            orderId, capture.Gross, capture.Fee, capture.Net);
        return order;
    }

    public async Task<Order> CancelAsync(int orderId, CancellationToken cancellationToken = default)
    {
        var order = await LoadAnyOrderAsync(orderId, cancellationToken);

        if (order.Status == OrderStatus.Cancelled)
        {
            return order; // idempotent
        }
        if (order.Status != OrderStatus.Authorized)
        {
            throw new PaymentConflictException(
                $"Order {orderId} cannot be cancelled because it is {order.Status}. " +
                "Only an authorized, unfulfilled order can be cancelled.");
        }

        // The authorization id is globally unique, so it keys void/capture/reauth idempotency safely
        // across runs (unlike the reused order id).
        await _gateway.VoidAsync(order.Payment!.AuthorizationId, idempotencyKey: order.Payment!.AuthorizationId, cancellationToken);

        order.MarkCancelled();
        await _orderRepository.UpdateAsync(order, cancellationToken);
        _logger.LogInformation("Cancelled order {OrderId}; held funds released.", orderId);
        return order;
    }

    public async Task<PaymentRefund> RefundAsync(
        int orderId, string buyerId, decimal? amount, string idempotencyKey, CancellationToken cancellationToken = default)
    {
        Guard.Against.NullOrEmpty(idempotencyKey, nameof(idempotencyKey));
        var order = await LoadOwnedOrderAsync(orderId, buyerId, cancellationToken);

        if (order.Status is not (OrderStatus.Fulfilled or OrderStatus.PartiallyRefunded))
        {
            throw new PaymentConflictException(
                $"Order {orderId} cannot be refunded because it is {order.Status}. " +
                "Only a fulfilled order can be refunded.");
        }

        var payment = order.Payment!;

        // Idempotent: the same key returns the same refund without refunding again.
        var existing = payment.Refunds.FirstOrDefault(r => r.IdempotencyKey == idempotencyKey);
        if (existing is not null)
        {
            return existing;
        }

        var remaining = payment.RefundableRemaining;
        if (remaining <= 0m)
        {
            throw new PaymentConflictException($"Order {orderId} has already been fully refunded.");
        }

        var resolved = amount ?? remaining;
        if (resolved <= 0m)
        {
            throw new InvalidPaymentRequestException("A refund amount must be greater than zero.");
        }
        if (resolved > remaining)
        {
            throw new PaymentConflictException(
                $"Refund of {resolved:0.00} exceeds the {remaining:0.00} still refundable on order {orderId}.");
        }

        var result = await _gateway.RefundAsync(
            payment.CaptureId!, resolved, payment.Currency, idempotencyKey, cancellationToken);

        var refund = order.AddRefund(result.RefundId, result.Amount, result.Status, idempotencyKey);
        await _orderRepository.UpdateAsync(order, cancellationToken);
        _logger.LogInformation(
            "Refunded {Amount} on order {OrderId} (refund {RefundId}); order is now {Status}.",
            result.Amount, orderId, result.RefundId, order.Status);
        return refund;
    }

    public async Task<IReadOnlyList<Order>> GetMyOrdersAsync(string buyerId, CancellationToken cancellationToken = default)
    {
        var orders = await _orderRepository.ListAsync(
            new CustomerOrdersWithPaymentSpecification(buyerId), cancellationToken);
        return orders;
    }

    // --- internals -----------------------------------------------------------

    private async Task RenewAuthorizationAsync(Order order, decimal amount, string currency, CancellationToken ct)
    {
        var reauth = await _gateway.ReauthorizeAsync(
            order.Payment!.AuthorizationId, amount, currency, idempotencyKey: order.Payment!.AuthorizationId, ct);
        order.RenewAuthorization(reauth.AuthorizationId, reauth.Status, reauth.ExpiresAt);
        await _orderRepository.UpdateAsync(order, ct);
        _logger.LogInformation("Renewed authorization for order {OrderId}: {AuthorizationId}.", order.Id, reauth.AuthorizationId);
    }

    private Task<PayPalCaptureResult> CaptureAsync(
        string authorizationId, decimal amount, string currency, int orderId, CancellationToken ct) =>
        // referenceId ($"order-{orderId}") correlates the transaction to the order; the authorization id
        // keys capture idempotency (globally unique, so no cross-run collision).
        _gateway.CaptureAsync(authorizationId, amount, currency, $"order-{orderId}", authorizationId, ct);

    private async Task<(RawCard? Card, string? VaultId, string Description)> ResolveInstrumentAsync(
        string buyerId, PaymentInstruction instruction, CancellationToken ct)
    {
        var hasCard = instruction.Card is not null;
        var hasSaved = instruction.SavedPaymentMethodId is not null;
        if (hasCard == hasSaved)
        {
            throw new InvalidPaymentRequestException(
                "Provide either card details or the id of a saved card (exactly one).");
        }

        if (hasSaved)
        {
            var buyer = await _buyerRepository.FirstOrDefaultAsync(
                new BuyerWithPaymentMethodsSpecification(buyerId), ct);
            var method = buyer?.FindPaymentMethod(instruction.SavedPaymentMethodId!.Value)
                ?? throw new PaymentEntityNotFoundException(
                    $"Saved card {instruction.SavedPaymentMethodId} was not found.");
            return (null, method.CardId, $"{method.Brand} ending {method.Last4}");
        }

        var card = instruction.Card!;
        return (card, null, $"Card ending {Last4Of(card.Number)}");
    }

    private async Task<Order> LoadOwnedOrderAsync(int orderId, string buyerId, CancellationToken ct)
    {
        var order = await _orderRepository.FirstOrDefaultAsync(
            new OrderWithPaymentByIdSpec(orderId, buyerId), ct);
        return order ?? throw new PaymentEntityNotFoundException($"Order {orderId} was not found.");
    }

    private async Task<Order> LoadAnyOrderAsync(int orderId, CancellationToken ct)
    {
        var order = await _orderRepository.FirstOrDefaultAsync(
            new OrderWithPaymentByIdSpec(orderId), ct);
        return order ?? throw new PaymentEntityNotFoundException($"Order {orderId} was not found.");
    }

    private static string Last4Of(string number)
    {
        var digits = new string(number.Where(char.IsDigit).ToArray());
        return digits.Length >= 4 ? digits[^4..] : digits;
    }

    private static Address ToAddress(ShippingAddressInput? input) =>
        new(
            input?.Street is { Length: > 0 } s ? s : "Unspecified",
            input?.City is { Length: > 0 } c ? c : "Unspecified",
            input?.State ?? string.Empty,
            input?.Country is { Length: > 0 } co ? co : "US",
            input?.ZipCode is { Length: > 0 } z ? z : "00000");
}
