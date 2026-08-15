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
using Microsoft.eShopWeb.ApplicationCore.Interfaces.Payments;
using Microsoft.eShopWeb.ApplicationCore.Specifications;

namespace Microsoft.eShopWeb.ApplicationCore.Services;

/// <summary>
/// Orchestrates the order + payment lifecycle: place → authorize (hold) → fulfil (capture) with
/// stale-hold renewal → cancel (void) → refund. All money amounts derive from catalog prices and the
/// configured currency; PayPal-owned ids/status are persisted on the order's <see cref="Payment"/>.
/// </summary>
public class PaymentProcessingService : IPaymentProcessingService
{
    // The additive order flow is API-driven and carries no shipping address, but the existing order
    // model requires one. Use a clearly-marked placeholder so orders remain valid.
    private static readonly Func<Address> DefaultShipToAddress =
        () => new Address("N/A", "N/A", "N/A", "N/A", "00000");

    private readonly IRepository<Order> _orderRepository;
    private readonly IReadRepository<CatalogItem> _itemRepository;
    private readonly IRepository<Buyer> _buyerRepository;
    private readonly IPayPalPaymentGateway _gateway;
    private readonly IPaymentCurrencyProvider _currencyProvider;
    private readonly IAppLogger<PaymentProcessingService> _logger;

    public PaymentProcessingService(
        IRepository<Order> orderRepository,
        IReadRepository<CatalogItem> itemRepository,
        IRepository<Buyer> buyerRepository,
        IPayPalPaymentGateway gateway,
        IPaymentCurrencyProvider currencyProvider,
        IAppLogger<PaymentProcessingService> logger)
    {
        _orderRepository = orderRepository;
        _itemRepository = itemRepository;
        _buyerRepository = buyerRepository;
        _gateway = gateway;
        _currencyProvider = currencyProvider;
        _logger = logger;
    }

    public async Task<Order> PlaceOrderAsync(string buyerId, IReadOnlyList<OrderLineRequest> lines, Address? shipToAddress, CancellationToken cancellationToken = default)
    {
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));
        if (lines is null || lines.Count == 0)
        {
            throw new PaymentException("An order must contain at least one item.");
        }
        if (lines.Any(l => l.Quantity <= 0))
        {
            throw new PaymentException("Every order line must have a quantity of at least 1.");
        }

        var ids = lines.Select(l => l.CatalogItemId).Distinct().ToArray();
        var catalogItems = await _itemRepository.ListAsync(new CatalogItemsSpecification(ids), cancellationToken);

        var items = new List<OrderItem>();
        foreach (var line in lines)
        {
            var catalogItem = catalogItems.FirstOrDefault(c => c.Id == line.CatalogItemId);
            if (catalogItem is null)
            {
                throw new PaymentException($"Catalog item {line.CatalogItemId} does not exist.");
            }

            var itemOrdered = new CatalogItemOrdered(catalogItem.Id, catalogItem.Name, catalogItem.PictureUri);
            // Unit price comes from the catalog, never the caller.
            items.Add(new OrderItem(itemOrdered, catalogItem.Price, line.Quantity));
        }

        var order = new Order(buyerId, shipToAddress ?? DefaultShipToAddress(), items);
        await _orderRepository.AddAsync(order, cancellationToken);
        _logger.LogInformation("Placed order {0} for buyer with {1} line(s).", order.Id, items.Count);
        return order;
    }

    public async Task<Order> AuthorizeOrderAsync(string buyerId, int orderId, PaymentInstrument instrument, CancellationToken cancellationToken = default)
    {
        var order = await _orderRepository.FirstOrDefaultAsync(new OrderWithPaymentByIdSpec(orderId, buyerId), cancellationToken);
        if (order is null)
        {
            throw new OrderNotFoundException(orderId);
        }

        // Idempotency: a repeat (double-click) after a hold is already in place must not authorize again.
        if (order.Payment is not null)
        {
            return order;
        }
        if (order.Status == OrderStatus.Cancelled)
        {
            throw new PaymentException($"Order {orderId} has been cancelled and can no longer be paid.");
        }

        var (card, vaultId) = await ResolveInstrumentAsync(buyerId, instrument, cancellationToken);

        var amount = order.Total();
        if (amount <= 0m)
        {
            throw new PaymentException($"Order {orderId} has no payable total.");
        }
        var currency = _currencyProvider.Currency;
        var reference = PaymentReference.ForOrder(orderId);

        var request = new AuthorizeGatewayRequest(
            ReferenceId: reference,
            CustomId: orderId.ToString(),
            Amount: amount,
            Currency: currency,
            Card: card,
            VaultId: vaultId,
            IdempotencyKey: PaymentReference.AuthorizeKey(orderId));

        var result = await _gateway.AuthorizeOrderAsync(request, cancellationToken);

        if (result.RequiresPayerAction)
        {
            throw new PaymentChallengeRequiredException(
                $"PayPal requires the shopper to approve this card payment in a browser (order status {result.OrderStatus}). " +
                "This integration does not perform a browser approval round-trip; use a card that authorizes without a challenge.");
        }
        if (string.IsNullOrEmpty(result.AuthorizationId))
        {
            throw new PaymentException(
                $"PayPal did not authorize order {orderId} (PayPal order status {result.OrderStatus}).");
        }

        var payment = new Payment(reference, amount, currency, result.PayPalOrderId, PaymentReference.AuthorizeKey(orderId));
        payment.SetAuthorization(result.AuthorizationId, result.AuthorizationStatus ?? "CREATED", result.ExpiresAt);
        order.AttachPayment(payment);

        await _orderRepository.UpdateAsync(order, cancellationToken);
        _logger.LogInformation("Authorized order {0}: hold {1}.", orderId, result.AuthorizationId);
        return order;
    }

    public async Task<Order> FulfilOrderAsync(int orderId, CancellationToken cancellationToken = default)
    {
        var order = await _orderRepository.FirstOrDefaultAsync(new OrderWithPaymentByIdSpec(orderId), cancellationToken);
        if (order is null)
        {
            throw new OrderNotFoundException(orderId);
        }

        // Idempotency: already fulfilled → no second capture.
        if (order.Status == OrderStatus.Paid && order.Payment?.CaptureId is not null)
        {
            return order;
        }
        if (order.Status != OrderStatus.PaymentAuthorized || order.Payment?.AuthorizationId is null)
        {
            throw new PaymentException($"Order {orderId} is not awaiting fulfilment (status {order.Status}).");
        }

        var payment = order.Payment;
        var capture = await CaptureWithRenewalAsync(order, payment, cancellationToken);

        payment.SetCapture(capture.CaptureId, capture.Status, capture.Gross, capture.PayPalFee, capture.Net);
        order.MarkPaid();

        await _orderRepository.UpdateAsync(order, cancellationToken);
        _logger.LogInformation("Fulfilled order {0}: capture {1} (net {2}).", orderId, capture.CaptureId, capture.Net);
        return order;
    }

    public async Task<Order> CancelOrderAsync(int orderId, CancellationToken cancellationToken = default)
    {
        var order = await _orderRepository.FirstOrDefaultAsync(new OrderWithPaymentByIdSpec(orderId), cancellationToken);
        if (order is null)
        {
            throw new OrderNotFoundException(orderId);
        }

        if (order.Status == OrderStatus.Cancelled)
        {
            return order; // idempotent
        }
        if (order.Status == OrderStatus.Paid)
        {
            throw new PaymentException($"Order {orderId} has been fulfilled and cannot be cancelled; issue a refund instead.");
        }

        // Release the held funds if a hold exists.
        if (order.Payment?.AuthorizationId is { Length: > 0 } authorizationId &&
            order.Payment.Status == PaymentStatus.Authorized)
        {
            await _gateway.VoidAuthorizationAsync(authorizationId, cancellationToken);
            order.Payment.MarkVoided();
        }

        order.MarkCancelled();
        await _orderRepository.UpdateAsync(order, cancellationToken);
        _logger.LogInformation("Cancelled order {0}.", orderId);
        return order;
    }

    public async Task<PaymentRefund> RefundOrderAsync(string buyerId, int orderId, decimal? amount, string idempotencyKey, CancellationToken cancellationToken = default)
    {
        Guard.Against.NullOrEmpty(idempotencyKey, nameof(idempotencyKey));

        var order = await _orderRepository.FirstOrDefaultAsync(new OrderWithPaymentByIdSpec(orderId, buyerId), cancellationToken);
        if (order is null)
        {
            throw new OrderNotFoundException(orderId);
        }

        var payment = order.Payment;
        if (payment?.CaptureId is null || order.Status != OrderStatus.Paid)
        {
            throw new PaymentException($"Order {orderId} has no captured payment to refund.");
        }

        // Idempotency: the same caller key must never refund twice.
        var existing = payment.FindRefundByIdempotencyKey(idempotencyKey);
        if (existing is not null)
        {
            return existing;
        }

        var remaining = payment.RefundableRemaining;
        if (remaining <= 0m)
        {
            throw new PaymentException($"Order {orderId} has already been fully refunded.");
        }

        var effective = amount ?? remaining;
        if (effective <= 0m)
        {
            throw new PaymentException("A refund amount must be greater than zero.");
        }
        // A partly-refunded order must never become refundable beyond what was captured.
        if (effective > remaining)
        {
            throw new PaymentException(
                $"Refund of {effective:0.00} exceeds the refundable balance of {remaining:0.00} for order {orderId}.");
        }

        var payPalKey = PaymentReference.RefundKey(payment.Reference, idempotencyKey);
        var result = await _gateway.RefundCaptureAsync(payment.CaptureId, effective, payment.Currency, payPalKey, cancellationToken);

        var refund = payment.AddRefund(result.RefundId, effective, result.Status, idempotencyKey);
        await _orderRepository.UpdateAsync(order, cancellationToken);
        _logger.LogInformation("Refunded order {0}: refund {1} amount {2}.", orderId, result.RefundId, effective);
        return refund;
    }

    public async Task<IReadOnlyList<Order>> GetOrdersForBuyerAsync(string buyerId, CancellationToken cancellationToken = default)
    {
        var orders = await _orderRepository.ListAsync(new CustomerOrdersWithPaymentSpecification(buyerId), cancellationToken);
        return orders;
    }

    public async Task<Order?> GetOrderForBuyerAsync(string buyerId, int orderId, CancellationToken cancellationToken = default)
    {
        return await _orderRepository.FirstOrDefaultAsync(new OrderWithPaymentByIdSpec(orderId, buyerId), cancellationToken);
    }

    private async Task<(CardDetails? Card, string? VaultId)> ResolveInstrumentAsync(string buyerId, PaymentInstrument instrument, CancellationToken cancellationToken)
    {
        var hasCard = instrument.Card is not null;
        var hasSaved = instrument.SavedCardId.HasValue;

        if (hasCard == hasSaved)
        {
            throw new PaymentException("Provide either card details or a saved card id, but not both.");
        }

        if (hasCard)
        {
            return (instrument.Card, null);
        }

        var buyer = await _buyerRepository.FirstOrDefaultAsync(new BuyerWithPaymentMethodsSpecification(buyerId), cancellationToken);
        var savedCard = buyer?.FindPaymentMethod(instrument.SavedCardId!.Value);
        if (savedCard is null)
        {
            // Scoped to the caller: an id that isn't theirs is indistinguishable from one that doesn't exist.
            throw new PaymentException($"Saved card {instrument.SavedCardId} was not found for this shopper.");
        }

        return (null, savedCard.VaultId);
    }

    private async Task<GatewayCapture> CaptureWithRenewalAsync(Order order, Payment payment, CancellationToken cancellationToken)
    {
        // Proactively renew a hold that has already gone stale.
        if (payment.AuthorizationExpiresAt is { } expiresAt && expiresAt <= DateTimeOffset.UtcNow)
        {
            await RenewAuthorizationAsync(order, payment, cancellationToken);
        }

        try
        {
            return await _gateway.CaptureAuthorizationAsync(
                payment.AuthorizationId!, payment.Amount, payment.Currency, PaymentReference.CaptureKey(order.Id), cancellationToken);
        }
        catch (PaymentGatewayException ex) when (IsStaleAuthorization(ex))
        {
            // The hold went stale between authorize and fulfilment; renew and capture the renewed hold.
            _logger.LogWarning("Authorization for order {0} is stale ({1}); renewing before capture.", order.Id, ex.Issue ?? ex.ErrorName ?? "expired");
            await RenewAuthorizationAsync(order, payment, cancellationToken);
            return await _gateway.CaptureAuthorizationAsync(
                payment.AuthorizationId!, payment.Amount, payment.Currency, PaymentReference.CaptureKey(order.Id), cancellationToken);
        }
    }

    private async Task RenewAuthorizationAsync(Order order, Payment payment, CancellationToken cancellationToken)
    {
        try
        {
            var renewed = await _gateway.ReauthorizeAsync(
                payment.AuthorizationId!, payment.Amount, payment.Currency, PaymentReference.ReauthorizeKey(order.Id), cancellationToken);

            if (string.IsNullOrEmpty(renewed.AuthorizationId))
            {
                throw new PaymentException($"PayPal did not return a renewed authorization for order {order.Id}.");
            }

            payment.RenewAuthorization(renewed.AuthorizationId, renewed.AuthorizationStatus ?? "CREATED", renewed.ExpiresAt);
            await _orderRepository.UpdateAsync(order, cancellationToken);
        }
        catch (PaymentGatewayException ex)
        {
            // The hold can no longer be renewed — say so in terms an operator can act on.
            throw new PaymentException(
                $"The authorization for order {order.Id} has expired and could not be renewed " +
                $"({ex.ErrorName}{(ex.Issue is null ? "" : "/" + ex.Issue)}). " +
                $"The held funds are gone; the shopper must place and pay for a new order. PayPal debug id: {ex.DebugId ?? "n/a"}.",
                ex);
        }
    }

    private static bool IsStaleAuthorization(PaymentGatewayException ex)
    {
        if (ex.Issue is not null && StaleIssues.Contains(ex.Issue)) return true;
        foreach (var issue in ex.Issues)
        {
            if (StaleIssues.Contains(issue)) return true;
        }
        return false;
    }

    // PayPal reports a hold that has aged past its honor period as AUTHORIZATION_EXPIRED on capture.
    private static readonly HashSet<string> StaleIssues = new(StringComparer.OrdinalIgnoreCase)
    {
        "AUTHORIZATION_EXPIRED",
    };
}
