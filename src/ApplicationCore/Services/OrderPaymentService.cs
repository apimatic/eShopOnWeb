using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Ardalis.GuardClauses;
using Microsoft.eShopWeb.ApplicationCore.Entities;
using Microsoft.eShopWeb.ApplicationCore.Entities.BuyerAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Specifications;

namespace Microsoft.eShopWeb.ApplicationCore.Services;

public class OrderPaymentService : IOrderPaymentService
{
    // Renew a hold if it expires within this window, so capture does not race expiry.
    private static readonly TimeSpan StaleWindow = TimeSpan.FromMinutes(5);

    private readonly IRepository<Order> _orderRepository;
    private readonly IRepository<CatalogItem> _itemRepository;
    private readonly IRepository<Buyer> _buyerRepository;
    private readonly IPayPalClient _payPal;
    private readonly IUriComposer _uriComposer;
    private readonly IPaymentConfiguration _config;
    private readonly IAppLogger<OrderPaymentService> _logger;

    public OrderPaymentService(
        IRepository<Order> orderRepository,
        IRepository<CatalogItem> itemRepository,
        IRepository<Buyer> buyerRepository,
        IPayPalClient payPal,
        IUriComposer uriComposer,
        IPaymentConfiguration config,
        IAppLogger<OrderPaymentService> logger)
    {
        _orderRepository = orderRepository;
        _itemRepository = itemRepository;
        _buyerRepository = buyerRepository;
        _payPal = payPal;
        _uriComposer = uriComposer;
        _config = config;
        _logger = logger;
    }

    public async Task<Order> CreateOrderAsync(string buyerId, IReadOnlyList<OrderLine> lines, Address shipToAddress, CancellationToken cancellationToken = default)
    {
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));
        if (lines is null || lines.Count == 0)
            throw new PaymentValidationException("An order must contain at least one item.");
        if (lines.Any(l => l.Quantity <= 0))
            throw new PaymentValidationException("Every item quantity must be a positive whole number.");

        var ids = lines.Select(l => l.CatalogItemId).Distinct().ToArray();
        var catalogItems = await _itemRepository.ListAsync(new CatalogItemsSpecification(ids), cancellationToken);

        var items = new List<OrderItem>();
        foreach (var line in lines)
        {
            var catalogItem = catalogItems.FirstOrDefault(c => c.Id == line.CatalogItemId)
                ?? throw new PaymentValidationException($"Catalog item {line.CatalogItemId} does not exist.");

            var itemOrdered = new CatalogItemOrdered(catalogItem.Id, catalogItem.Name, _uriComposer.ComposePicUri(catalogItem.PictureUri));
            items.Add(new OrderItem(itemOrdered, catalogItem.Price, line.Quantity));
        }

        var order = new Order(buyerId, shipToAddress, items);
        await _orderRepository.AddAsync(order, cancellationToken);

        _logger.LogInformation($"Order {order.Id} placed by {buyerId} for {order.Total():0.00} {_config.Currency}.");
        return order;
    }

    public async Task<Order> PayAsync(int orderId, string buyerId, PaymentInstrument instrument, CancellationToken cancellationToken = default)
    {
        var order = await LoadOwnedOrderAsync(orderId, buyerId, cancellationToken);

        if (order.Status is OrderStatus.Fulfilled or OrderStatus.Cancelled)
            throw new PaymentConflictException($"Order {orderId} is {order.Status} and can no longer be paid.");

        // Idempotent in effect: a double-click after a successful authorization returns the existing hold.
        if (order.Status == OrderStatus.PaymentAuthorized && order.Payment is not null)
        {
            _logger.LogInformation($"Order {orderId} is already authorized (PayPal auth {order.Payment.AuthorizationId}); returning existing hold.");
            return order;
        }

        var (card, vaultId) = await ResolveInstrumentAsync(buyerId, instrument, cancellationToken);

        var amount = RoundedTotal(order);
        if (amount <= 0m)
            throw new PaymentConflictException($"Order {orderId} has a zero total and cannot be paid.");

        var requestId = Guid.NewGuid().ToString("N");
        var authorizeRequest = new PayPalAuthorizeRequest
        {
            Amount = amount,
            Currency = _config.Currency,
            InvoiceId = InvoiceIdFor(order),
            RequestId = requestId,
            Card = card,
            VaultId = vaultId,
            Description = $"eShopOnWeb order {order.Id}"
        };

        var auth = await _payPal.AuthorizeAsync(authorizeRequest, cancellationToken);

        // The amount PayPal holds must equal the order total to the cent.
        if (decimal.Round(auth.Amount, 2) != decimal.Round(amount, 2))
        {
            throw new PayPalApiException(
                $"PayPal authorized {auth.Amount:0.00} {auth.Currency} but the order total is {amount:0.00} {_config.Currency}.",
                502);
        }

        var payment = new OrderPayment(
            auth.PayPalOrderId,
            auth.AuthorizationId,
            auth.AuthorizationStatus,
            auth.Amount,
            auth.Currency,
            auth.ExpiresAt,
            requestId);

        order.RecordAuthorization(payment);
        await _orderRepository.UpdateAsync(order, cancellationToken);

        _logger.LogInformation($"Order {orderId} authorized: PayPal order {auth.PayPalOrderId}, auth {auth.AuthorizationId}, {auth.Amount:0.00} {auth.Currency}.");
        return order;
    }

    public async Task<Order> FulfilAsync(int orderId, CancellationToken cancellationToken = default)
    {
        var order = await LoadOrderAsync(orderId, cancellationToken);

        if (order.Status == OrderStatus.Fulfilled)
        {
            _logger.LogInformation($"Order {orderId} is already fulfilled; returning existing capture.");
            return order; // idempotent
        }

        if (order.Status != OrderStatus.PaymentAuthorized || order.Payment is null)
            throw new PaymentConflictException($"Order {orderId} is {order.Status}; it must be authorized before it can be fulfilled.");

        var capture = await CaptureWithRenewalAsync(order, cancellationToken);

        order.RecordFulfilment(capture.CaptureId, capture.Status, capture.GrossAmount, capture.PayPalFee, capture.NetAmount);
        await _orderRepository.UpdateAsync(order, cancellationToken);

        _logger.LogInformation(
            $"Order {orderId} fulfilled: capture {capture.CaptureId}, captured {capture.GrossAmount:0.00}, fee {capture.PayPalFee:0.00}, net {capture.NetAmount:0.00} {capture.Currency}.");
        return order;
    }

    public async Task<Order> CancelAsync(int orderId, CancellationToken cancellationToken = default)
    {
        var order = await LoadOrderAsync(orderId, cancellationToken);

        if (order.Status == OrderStatus.Cancelled)
            return order; // idempotent

        if (order.Status == OrderStatus.Fulfilled)
            throw new PaymentConflictException(
                $"Order {orderId} has been fulfilled and its funds captured. Issue a refund instead of a cancellation.");

        // Release the hold with PayPal if one exists.
        if (order.Status == OrderStatus.PaymentAuthorized && order.Payment is not null)
        {
            try
            {
                await _payPal.VoidAuthorizationAsync(order.Payment.AuthorizationId, $"void-{order.Payment.AuthorizationId}", cancellationToken);
            }
            catch (PayPalApiException ex) when (ex.PayPalStatusCode == 422 && ex.HasIssue("AUTHORIZATION_ALREADY_CAPTURED"))
            {
                throw new PaymentConflictException(
                    $"Order {orderId} cannot be cancelled: the authorization was already captured. Issue a refund instead.");
            }
        }

        order.RecordCancellation();
        await _orderRepository.UpdateAsync(order, cancellationToken);

        _logger.LogInformation($"Order {orderId} cancelled; any hold was released.");
        return order;
    }

    public async Task<OrderRefund> RefundAsync(int orderId, string buyerId, decimal? amount, string idempotencyKey, CancellationToken cancellationToken = default)
    {
        Guard.Against.NullOrEmpty(idempotencyKey, nameof(idempotencyKey));

        var order = await LoadOwnedOrderAsync(orderId, buyerId, cancellationToken);

        if (order.Payment is null || !order.Payment.IsCaptured)
            throw new PaymentConflictException($"Order {orderId} has not been fulfilled, so its payment cannot be refunded.");

        // Idempotent in effect: repeating a request under the same key never refunds twice.
        var existing = order.Payment.FindRefundByIdempotencyKey(idempotencyKey);
        if (existing is not null)
        {
            _logger.LogInformation($"Refund for order {orderId} with key {idempotencyKey} already exists ({existing.PayPalRefundId}); returning it.");
            return existing;
        }

        // A null amount means "refund whatever is still refundable".
        var refundAmount = amount ?? order.Payment.RefundableRemaining;
        order.Payment.GuardRefundable(refundAmount);

        // Send the explicit amount only for partial refunds; a full refund uses an empty body.
        var isFullRemaining = decimal.Round(refundAmount, 2) == decimal.Round(order.Payment.RefundableRemaining, 2);
        decimal? amountToSend = isFullRemaining ? (decimal?)null : refundAmount;

        var result = await _payPal.RefundCaptureAsync(order.Payment.CaptureId!, amountToSend, _config.Currency, idempotencyKey, cancellationToken);

        var refund = order.RecordRefund(result.RefundId, result.Amount, result.Status, idempotencyKey);
        await _orderRepository.UpdateAsync(order, cancellationToken);

        _logger.LogInformation($"Order {orderId} refunded {result.Amount:0.00} {result.Currency} (refund {result.RefundId}, key {idempotencyKey}).");
        return refund;
    }

    public async Task<Order> GetOrderForBuyerAsync(int orderId, string buyerId, CancellationToken cancellationToken = default) =>
        await LoadOwnedOrderAsync(orderId, buyerId, cancellationToken);

    public async Task<IReadOnlyList<Order>> GetOrdersForBuyerAsync(string buyerId, CancellationToken cancellationToken = default)
    {
        var orders = await _orderRepository.ListAsync(new CustomerOrdersWithItemsSpecification(buyerId), cancellationToken);
        return orders.OrderByDescending(o => o.OrderDate).ToList();
    }

    // --- helpers ---

    private async Task<Order> LoadOrderAsync(int orderId, CancellationToken cancellationToken)
    {
        var order = await _orderRepository.FirstOrDefaultAsync(new OrderWithItemsByIdSpec(orderId), cancellationToken);
        return order ?? throw new EntityNotFoundException("Order", orderId);
    }

    private async Task<Order> LoadOwnedOrderAsync(int orderId, string buyerId, CancellationToken cancellationToken)
    {
        var order = await _orderRepository.FirstOrDefaultAsync(new OrderWithItemsByIdSpec(orderId), cancellationToken);
        // One shopper must never see or act on another's order: hide existence entirely.
        if (order is null || !string.Equals(order.BuyerId, buyerId, StringComparison.Ordinal))
            throw new EntityNotFoundException("Order", orderId);
        return order;
    }

    private async Task<(PayPalCardDetails? card, string? vaultId)> ResolveInstrumentAsync(string buyerId, PaymentInstrument instrument, CancellationToken cancellationToken)
    {
        var hasCard = instrument.Card is not null;
        var hasSaved = instrument.SavedPaymentMethodId.HasValue;

        if (hasCard == hasSaved)
            throw new PaymentValidationException("Provide either card details or a saved paymentMethodId, but not both.");

        if (hasCard)
            return (instrument.Card, null);

        // Resolve a saved card. Only the caller's own cards are reachable, so cross-shopper use is impossible.
        var buyer = await _buyerRepository.FirstOrDefaultAsync(new BuyerWithPaymentMethodsSpecification(buyerId), cancellationToken);
        var method = buyer?.FindPaymentMethod(instrument.SavedPaymentMethodId!.Value);
        if (method is null)
            throw new EntityNotFoundException("Saved card", instrument.SavedPaymentMethodId!.Value);

        return (null, method.PayPalVaultId);
    }

    private async Task<PayPalCaptureResult> CaptureWithRenewalAsync(Order order, CancellationToken cancellationToken)
    {
        var amount = RoundedTotal(order);
        var payment = order.Payment!;

        // Proactively renew a hold that is already stale so fulfilment doesn't fail outright.
        if (IsStale(payment))
        {
            _logger.LogInformation($"Order {order.Id} authorization {payment.AuthorizationId} is stale; renewing before capture.");
            await RenewAuthorizationAsync(order, amount, cancellationToken);
        }

        var authId = order.Payment!.AuthorizationId;
        try
        {
            return await _payPal.CaptureAuthorizationAsync(authId, amount, _config.Currency, $"capture-{authId}", cancellationToken);
        }
        catch (PayPalApiException ex) when (IsExpiredAuthorization(ex))
        {
            // React to an expiry we didn't catch proactively: renew, then capture the fresh hold.
            _logger.LogInformation($"Order {order.Id} capture reported an expired authorization; renewing and retrying.");
            await RenewAuthorizationAsync(order, amount, cancellationToken);
            var freshId = order.Payment!.AuthorizationId;
            return await _payPal.CaptureAuthorizationAsync(freshId, amount, _config.Currency, $"capture-{freshId}", cancellationToken);
        }
    }

    private async Task RenewAuthorizationAsync(Order order, decimal amount, CancellationToken cancellationToken)
    {
        var authId = order.Payment!.AuthorizationId;
        try
        {
            var reauth = await _payPal.ReauthorizeAsync(authId, amount, _config.Currency, $"reauth-{authId}", cancellationToken);
            order.RecordReauthorization(reauth.AuthorizationId, reauth.AuthorizationStatus, reauth.ExpiresAt);
        }
        catch (PayPalApiException ex)
        {
            var detail = ex.Issues.Count > 0 ? string.Join(", ", ex.Issues) : $"PayPal debug id {ex.DebugId}";
            throw new AuthorizationCannotBeRenewedException(
                $"The payment hold on order {order.Id} has expired and PayPal could not renew it ({detail}). " +
                "PayPal permits a single reauthorization only on days 4-29 after the original hold, and none after 30 days. " +
                "Ask the shopper to pay this order again to place a fresh hold, then fulfil it.");
        }
    }

    private static bool IsStale(OrderPayment payment) =>
        payment.AuthorizationExpiresAt.HasValue &&
        payment.AuthorizationExpiresAt.Value <= DateTimeOffset.UtcNow.Add(StaleWindow);

    private static bool IsExpiredAuthorization(PayPalApiException ex) =>
        ex.HasIssue("AUTHORIZATION_EXPIRED") || ex.HasIssue("INVALID_AUTHORIZATION_ID_STATUS");

    private decimal RoundedTotal(Order order) => decimal.Round(order.Total(), 2, MidpointRounding.AwayFromZero);

    private static string InvoiceIdFor(Order order) =>
        string.Create(CultureInfo.InvariantCulture, $"ESHOP-{order.Id}");
}
