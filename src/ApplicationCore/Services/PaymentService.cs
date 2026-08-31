using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Ardalis.GuardClauses;
using Microsoft.eShopWeb.ApplicationCore.Entities;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.SavedCardAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Payments;
using Microsoft.eShopWeb.ApplicationCore.Specifications;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.ApplicationCore.Services;

public class PaymentService : IPaymentService
{
    // Serializes payment operations per order so a double-click (or concurrent
    // fulfil + cancel) can never trigger two PayPal mutations for one order.
    private static readonly ConcurrentDictionary<int, SemaphoreSlim> _orderLocks = new();

    private readonly IRepository<Order> _orderRepository;
    private readonly IRepository<Payment> _paymentRepository;
    private readonly IRepository<CatalogItem> _itemRepository;
    private readonly IRepository<SavedCard> _savedCardRepository;
    private readonly IPaymentGateway _gateway;
    private readonly IUriComposer _uriComposer;
    private readonly IAppLogger<PaymentService> _logger;
    private readonly PaymentSettings _settings;

    public PaymentService(
        IRepository<Order> orderRepository,
        IRepository<Payment> paymentRepository,
        IRepository<CatalogItem> itemRepository,
        IRepository<SavedCard> savedCardRepository,
        IPaymentGateway gateway,
        IUriComposer uriComposer,
        IAppLogger<PaymentService> logger,
        IOptions<PaymentSettings> settings)
    {
        _orderRepository = orderRepository;
        _paymentRepository = paymentRepository;
        _itemRepository = itemRepository;
        _savedCardRepository = savedCardRepository;
        _gateway = gateway;
        _uriComposer = uriComposer;
        _logger = logger;
        _settings = settings.Value;
    }

    public async Task<Order> CreateOrderAsync(string buyerId, IReadOnlyList<OrderItemRequest> items,
        Address? shipToAddress, CancellationToken cancellationToken = default)
    {
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));
        if (items is null || items.Count == 0)
        {
            throw new ArgumentException("An order must contain at least one item.", nameof(items));
        }
        if (items.Any(i => i.Quantity <= 0))
        {
            throw new ArgumentException("Item quantities must be positive.", nameof(items));
        }

        var spec = new CatalogItemsSpecification(items.Select(i => i.CatalogItemId).ToArray());
        var catalogItems = await _itemRepository.ListAsync(spec, cancellationToken);

        var missing = items.Select(i => i.CatalogItemId).Distinct()
            .Except(catalogItems.Select(c => c.Id)).ToList();
        if (missing.Count > 0)
        {
            throw new ArgumentException($"Unknown catalog item id(s): {string.Join(", ", missing)}.");
        }

        var orderItems = items.Select(i =>
        {
            var catalogItem = catalogItems.First(c => c.Id == i.CatalogItemId);
            var itemOrdered = new CatalogItemOrdered(catalogItem.Id, catalogItem.Name,
                _uriComposer.ComposePicUri(catalogItem.PictureUri));
            return new OrderItem(itemOrdered, catalogItem.Price, i.Quantity);
        }).ToList();

        var address = shipToAddress ?? new Address("123 Main Street", "Kent", "OH", "United States", "44240");
        var order = new Order(buyerId, address, orderItems);
        return await _orderRepository.AddAsync(order, cancellationToken);
    }

    public async Task<Payment> PayOrderAsync(string buyerId, int orderId, CardDetails? card, int? paymentMethodId,
        CancellationToken cancellationToken = default)
    {
        if (card is null && paymentMethodId is null)
        {
            throw new ArgumentException("Provide either card details or a saved paymentMethodId.");
        }

        var gate = await LockOrderAsync(orderId, cancellationToken);
        try
        {
            var order = await GetOwnedOrderAsync(buyerId, orderId, cancellationToken);

            var payment = await _paymentRepository.FirstOrDefaultAsync(
                new PaymentByOrderIdSpecification(orderId), cancellationToken);

            // Idempotent replay: the order is already paid for.
            if (order.Status == OrderStatus.PaymentAuthorized && payment?.Status == PaymentStatus.Authorized)
            {
                return payment;
            }
            if (order.Status != OrderStatus.AwaitingPayment)
            {
                throw new OrderStateException($"Order {orderId} is {order.Status} and cannot be paid.");
            }

            string? vaultTokenId = null;
            if (paymentMethodId is not null)
            {
                var savedCard = await _savedCardRepository.FirstOrDefaultAsync(
                    new SavedCardByIdSpecification(paymentMethodId.Value), cancellationToken);
                if (savedCard is null || savedCard.BuyerId != buyerId)
                {
                    throw new SavedCardNotFoundException(paymentMethodId.Value);
                }
                vaultTokenId = savedCard.VaultTokenId;
            }

            payment ??= await _paymentRepository.AddAsync(
                new Payment(orderId, buyerId, order.Total(), _settings.Currency), cancellationToken);

            var amount = order.Total();
            GatewayAuthorizationResult result;
            try
            {
                result = vaultTokenId is not null
                    ? await _gateway.AuthorizeWithVaultedCardAsync(vaultTokenId, amount, payment.Currency,
                        payment.AuthorizeRequestId, $"eshop-order-{orderId}", cancellationToken)
                    : await _gateway.AuthorizeWithCardAsync(card!, amount, payment.Currency,
                        payment.AuthorizeRequestId, $"eshop-order-{orderId}", cancellationToken);
            }
            catch (PayPalApiException ex)
            {
                payment.MarkAuthorizationFailed(null, ex.ErrorName);
                await _paymentRepository.UpdateAsync(payment, cancellationToken);
                throw new PaymentException(
                    $"PayPal declined the authorization for order {orderId}: {ex.Message}", ex.ErrorName, ex);
            }

            if (string.Equals(result.OrderStatus, "PAYER_ACTION_REQUIRED", StringComparison.OrdinalIgnoreCase))
            {
                payment.MarkAuthorizationFailed(result.PayPalOrderId, result.OrderStatus);
                await _paymentRepository.UpdateAsync(payment, cancellationToken);
                throw new PaymentException(
                    "PayPal answered the card payment with a challenge requiring shopper approval in a browser " +
                    "(PAYER_ACTION_REQUIRED). This integration does not support approval round-trips.");
            }

            if (string.IsNullOrEmpty(result.AuthorizationId))
            {
                payment.MarkAuthorizationFailed(result.PayPalOrderId, result.OrderStatus);
                await _paymentRepository.UpdateAsync(payment, cancellationToken);
                throw new PaymentException(
                    $"PayPal did not return an authorization for order {orderId} (order status {result.OrderStatus}).");
            }

            payment.MarkAuthorized(result.PayPalOrderId, result.AuthorizationId,
                result.AuthorizationStatus ?? "UNKNOWN", result.ExpiresAt);
            order.MarkPaymentAuthorized();

            await _paymentRepository.UpdateAsync(payment, cancellationToken);
            await _orderRepository.UpdateAsync(order, cancellationToken);

            _logger.LogInformation($"Order {orderId} authorized at PayPal (authorization {result.AuthorizationId}).");
            return payment;
        }
        finally
        {
            gate.Release();
        }
    }

    public async Task<Payment> FulfilOrderAsync(int orderId, CancellationToken cancellationToken = default)
    {
        var gate = await LockOrderAsync(orderId, cancellationToken);
        try
        {
            var order = await GetOrderAsync(orderId, cancellationToken);
            var payment = await _paymentRepository.FirstOrDefaultAsync(
                new PaymentByOrderIdSpecification(orderId), cancellationToken);

            if (order.Status == OrderStatus.Fulfilled && payment?.Status is PaymentStatus.Captured
                or PaymentStatus.PartiallyRefunded or PaymentStatus.Refunded)
            {
                return payment; // idempotent replay
            }
            if (order.Status != OrderStatus.PaymentAuthorized || payment?.AuthorizationId is null)
            {
                throw new OrderStateException(
                    $"Order {orderId} cannot be fulfilled from state {order.Status}; it must be paid (authorized) first.");
            }

            var authorization = await _gateway.GetAuthorizationAsync(payment.AuthorizationId, cancellationToken);
            payment.MarkReauthorized(authorization.AuthorizationId, authorization.Status, authorization.ExpiresAt);

            if (!IsCapturable(authorization.Status))
            {
                // The hold went stale before fulfilment: renew it, then capture.
                _logger.LogInformation(
                    $"Authorization {authorization.AuthorizationId} for order {orderId} is {authorization.Status}; reauthorizing.");
                try
                {
                    authorization = await _gateway.ReauthorizeAsync(authorization.AuthorizationId,
                        payment.Amount, payment.Currency, Guid.NewGuid().ToString("N"), cancellationToken);
                    payment.MarkReauthorized(authorization.AuthorizationId, authorization.Status, authorization.ExpiresAt);
                }
                catch (PayPalApiException ex)
                {
                    await _paymentRepository.UpdateAsync(payment, cancellationToken);
                    throw new PaymentException(
                        $"The PayPal authorization for order {orderId} expired and could not be renewed " +
                        $"({ex.Message}). Ask the shopper to pay again (POST /api/orders/{orderId}/pay) before fulfilling.",
                        ex.ErrorName, ex);
                }
            }

            GatewayCaptureResult capture;
            try
            {
                capture = await _gateway.CaptureAuthorizationAsync(authorization.AuthorizationId,
                    payment.Amount, payment.Currency, payment.CaptureRequestId, cancellationToken);
            }
            catch (PayPalApiException ex)
            {
                await _paymentRepository.UpdateAsync(payment, cancellationToken);
                throw new PaymentException(
                    $"PayPal could not capture the funds for order {orderId}: {ex.Message}", ex.ErrorName, ex);
            }

            payment.MarkCaptured(capture.CaptureId, capture.Status, capture.Amount, capture.PayPalFee, capture.NetAmount);
            order.MarkFulfilled();

            await _paymentRepository.UpdateAsync(payment, cancellationToken);
            await _orderRepository.UpdateAsync(order, cancellationToken);

            _logger.LogInformation($"Order {orderId} fulfilled; captured {capture.Amount:0.00} {capture.Currency} " +
                                   $"(fee {capture.PayPalFee:0.00}, net {capture.NetAmount:0.00}).");
            return payment;
        }
        finally
        {
            gate.Release();
        }
    }

    public async Task<Payment?> CancelOrderAsync(int orderId, CancellationToken cancellationToken = default)
    {
        var gate = await LockOrderAsync(orderId, cancellationToken);
        try
        {
            var order = await GetOrderAsync(orderId, cancellationToken);
            var payment = await _paymentRepository.FirstOrDefaultAsync(
                new PaymentByOrderIdSpecification(orderId), cancellationToken);

            if (order.Status == OrderStatus.Cancelled)
            {
                return payment; // idempotent replay
            }

            if (payment?.Status == PaymentStatus.Authorized && payment.AuthorizationId is not null)
            {
                try
                {
                    await _gateway.VoidAuthorizationAsync(payment.AuthorizationId,
                        Guid.NewGuid().ToString("N"), cancellationToken);
                }
                catch (PayPalApiException ex)
                {
                    throw new PaymentException(
                        $"PayPal could not release the held funds for order {orderId}: {ex.Message}. " +
                        "Retry the cancel; until it succeeds the shopper's money remains on hold.",
                        ex.ErrorName, ex);
                }
                payment.MarkVoided();
                await _paymentRepository.UpdateAsync(payment, cancellationToken);
            }

            order.MarkCancelled();
            await _orderRepository.UpdateAsync(order, cancellationToken);

            _logger.LogInformation($"Order {orderId} cancelled; any held funds released.");
            return payment;
        }
        finally
        {
            gate.Release();
        }
    }

    public async Task<PaymentRefund> RefundOrderAsync(string buyerId, int orderId, decimal? amount,
        string idempotencyKey, string? noteToPayer, CancellationToken cancellationToken = default)
    {
        Guard.Against.NullOrEmpty(idempotencyKey, nameof(idempotencyKey));

        var gate = await LockOrderAsync(orderId, cancellationToken);
        try
        {
            var order = await GetOwnedOrderAsync(buyerId, orderId, cancellationToken);
            var payment = await _paymentRepository.FirstOrDefaultAsync(
                new PaymentByOrderIdSpecification(orderId), cancellationToken);

            if (payment is null || order.Status != OrderStatus.Fulfilled)
            {
                throw new OrderStateException(
                    $"Order {orderId} has no captured payment to refund; refunds are only possible after fulfilment.");
            }
            if (payment.Status == PaymentStatus.Refunded)
            {
                throw new OrderStateException(
                    $"Order {orderId} has already been fully refunded ({payment.TotalRefunded:0.00} {payment.Currency} " +
                    $"of {payment.CapturedAmount:0.00} {payment.Currency}); nothing remains refundable.");
            }
            if (payment.Status is not (PaymentStatus.Captured or PaymentStatus.PartiallyRefunded))
            {
                throw new OrderStateException(
                    $"Order {orderId} has no captured payment to refund; refunds are only possible after fulfilment.");
            }

            // Idempotent replay under the caller's key.
            var existing = payment.FindRefundByIdempotencyKey(idempotencyKey);
            if (existing is not null)
            {
                return existing;
            }

            var refundAmount = amount ?? payment.RefundableAmount;
            if (refundAmount <= 0 || refundAmount > payment.RefundableAmount)
            {
                throw new PaymentException(
                    $"Refund of {refundAmount:0.00} {payment.Currency} exceeds the refundable balance of " +
                    $"{payment.RefundableAmount:0.00} {payment.Currency} (captured {payment.CapturedAmount:0.00}, " +
                    $"already refunded {payment.TotalRefunded:0.00}).");
            }

            GatewayRefundResult result;
            try
            {
                // Scope the PayPal idempotency key to this capture so the same
                // caller key under a different capture (or merchant) stays distinct.
                var gatewayRequestId = $"eshop-{payment.CaptureId}-{idempotencyKey}";
                result = await _gateway.RefundCaptureAsync(payment.CaptureId!, refundAmount, payment.Currency,
                    gatewayRequestId, noteToPayer, cancellationToken);
            }
            catch (PayPalApiException ex)
            {
                throw new PaymentException(
                    $"PayPal could not refund order {orderId}: {ex.Message}", ex.ErrorName, ex);
            }

            var refund = payment.AddRefund(result.RefundId, result.Status, refundAmount, idempotencyKey, noteToPayer);
            await _paymentRepository.UpdateAsync(payment, cancellationToken);

            _logger.LogInformation($"Order {orderId} refunded {refundAmount:0.00} {payment.Currency} " +
                                   $"(PayPal refund {result.RefundId}).");
            return refund;
        }
        finally
        {
            gate.Release();
        }
    }

    public async Task<IReadOnlyList<Order>> GetOrdersForBuyerAsync(string buyerId, CancellationToken cancellationToken = default)
    {
        return await _orderRepository.ListAsync(new CustomerOrdersSpecification(buyerId), cancellationToken);
    }

    public async Task<Payment?> GetPaymentForOrderAsync(int orderId, CancellationToken cancellationToken = default)
    {
        return await _paymentRepository.FirstOrDefaultAsync(new PaymentByOrderIdSpecification(orderId), cancellationToken);
    }

    private static bool IsCapturable(string? status) =>
        string.Equals(status, "CREATED", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(status, "PENDING", StringComparison.OrdinalIgnoreCase);

    private async Task<Order> GetOrderAsync(int orderId, CancellationToken cancellationToken)
    {
        var order = await _orderRepository.FirstOrDefaultAsync(new OrderWithItemsByIdSpec(orderId), cancellationToken);
        return order ?? throw new OrderNotFoundException(orderId);
    }

    private async Task<Order> GetOwnedOrderAsync(string buyerId, int orderId, CancellationToken cancellationToken)
    {
        var order = await GetOrderAsync(orderId, cancellationToken);
        if (order.BuyerId != buyerId)
        {
            // Do not leak the existence of another shopper's order.
            throw new OrderNotFoundException(orderId);
        }
        return order;
    }

    private static async Task<SemaphoreSlim> LockOrderAsync(int orderId, CancellationToken cancellationToken)
    {
        var gate = _orderLocks.GetOrAdd(orderId, _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(cancellationToken);
        return gate;
    }
}
