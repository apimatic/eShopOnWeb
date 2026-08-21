using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Specifications;
using Microsoft.Extensions.Logging;

namespace Microsoft.eShopWeb.ApplicationCore.Services;

public class OrderCheckoutService : IOrderCheckoutService
{
    private static readonly TimeSpan HonorPeriod = TimeSpan.FromDays(3);
    private static readonly TimeSpan MaxAuthorizationLifetime = TimeSpan.FromDays(29);
    private static readonly ConcurrentDictionary<int, SemaphoreSlim> OrderGates = new();

    private readonly IRepository<Order> _orderRepository;
    private readonly IRepository<CatalogItem> _catalogItemRepository;
    private readonly IRepository<OrderPayment> _paymentRepository;
    private readonly IRepository<SavedPaymentMethod> _savedCardRepository;
    private readonly IPayPalGateway _payPal;
    private readonly IUriComposer _uriComposer;
    private readonly ILogger<OrderCheckoutService> _logger;

    public OrderCheckoutService(
        IRepository<Order> orderRepository,
        IRepository<CatalogItem> catalogItemRepository,
        IRepository<OrderPayment> paymentRepository,
        IRepository<SavedPaymentMethod> savedCardRepository,
        IPayPalGateway payPal,
        IUriComposer uriComposer,
        ILogger<OrderCheckoutService> logger)
    {
        _orderRepository = orderRepository;
        _catalogItemRepository = catalogItemRepository;
        _paymentRepository = paymentRepository;
        _savedCardRepository = savedCardRepository;
        _payPal = payPal;
        _uriComposer = uriComposer;
        _logger = logger;
    }

    public async Task<OrderDetailsDto> PlaceOrderAsync(
        string buyerId,
        IReadOnlyList<OrderLineRequest> items,
        Address? shipToAddress,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(buyerId))
        {
            throw new PaymentException("The caller is not authenticated.", HttpStatusCode.Unauthorized);
        }

        if (items == null || items.Count == 0)
        {
            throw new PaymentException("At least one catalog item is required.");
        }

        var quantities = new Dictionary<int, int>();
        foreach (var line in items)
        {
            if (line.CatalogItemId <= 0 || line.Quantity <= 0)
            {
                throw new PaymentException("Each item must include a catalogItemId and a quantity greater than zero.");
            }

            quantities[line.CatalogItemId] = quantities.TryGetValue(line.CatalogItemId, out var existing)
                ? existing + line.Quantity
                : line.Quantity;
        }

        var catalogItems = await _catalogItemRepository.ListAsync(
            new CatalogItemsSpecification(quantities.Keys.ToArray()),
            cancellationToken);

        if (catalogItems.Count != quantities.Count)
        {
            var found = catalogItems.Select(c => c.Id).ToHashSet();
            var missing = quantities.Keys.Where(id => !found.Contains(id)).ToArray();
            throw new PaymentException($"Catalog item(s) not found: {string.Join(", ", missing)}.", HttpStatusCode.NotFound);
        }

        var orderItems = catalogItems.Select(catalogItem =>
        {
            var pictureUri = string.IsNullOrWhiteSpace(catalogItem.PictureUri)
                ? "none"
                : _uriComposer.ComposePicUri(catalogItem.PictureUri);
            var itemOrdered = new CatalogItemOrdered(catalogItem.Id, catalogItem.Name, pictureUri);
            return new OrderItem(itemOrdered, catalogItem.Price, quantities[catalogItem.Id]);
        }).ToList();

        var address = shipToAddress ?? new Address("123 Main St.", "Anytown", "CA", "US", "12345");
        var order = new Order(buyerId, address, orderItems);
        await _orderRepository.AddAsync(order, cancellationToken);

        var payment = new OrderPayment(order.Id, buyerId, order.Total(), _payPal.Currency);
        await _paymentRepository.AddAsync(payment, cancellationToken);

        return Map(order, payment);
    }

    public async Task<OrderDetailsDto> PayAsync(
        string buyerId,
        int orderId,
        PayOrderCommand command,
        CancellationToken cancellationToken = default)
    {
        return await WithOrderLock(orderId, async () =>
        {
            var (order, payment) = await LoadOwnedOrderAsync(buyerId, orderId, cancellationToken);

            if (order.Status is OrderStatus.Authorized or OrderStatus.Fulfilled
                or OrderStatus.PartiallyRefunded or OrderStatus.Refunded)
            {
                return Map(order, payment);
            }

            if (order.Status == OrderStatus.Cancelled)
            {
                throw new PaymentException("A cancelled order cannot be paid.", HttpStatusCode.Conflict);
            }

            if (order.Status != OrderStatus.AwaitingPayment)
            {
                throw new PaymentException($"Order {orderId} cannot be paid in status '{order.Status}'.", HttpStatusCode.Conflict);
            }

            var hasCard = command.Card != null && !string.IsNullOrWhiteSpace(command.Card.Number);
            var hasSaved = command.PaymentMethodId.HasValue;
            if (hasCard == hasSaved)
            {
                throw new PaymentException("Provide either card details or a paymentMethodId, not both.");
            }

            string? vaultId = null;
            PayPalCardDetails? card = null;
            if (hasSaved)
            {
                var saved = await _savedCardRepository.FirstOrDefaultAsync(
                    new SavedPaymentMethodByIdSpec(command.PaymentMethodId!.Value, buyerId),
                    cancellationToken);
                if (saved == null)
                {
                    throw new PaymentException("Saved payment method was not found.", HttpStatusCode.NotFound);
                }

                vaultId = saved.PayPalPaymentTokenId;
            }
            else
            {
                card = ToPayPalCard(command.Card!);
            }

            var amount = decimal.Round(order.Total(), 2, MidpointRounding.AwayFromZero);
            if (ToCents(amount) != ToCents(payment.Amount))
            {
                throw new PaymentException("The payment amount does not match the order total.", HttpStatusCode.Conflict);
            }

            var invoiceId = $"ESHOP-{order.Id}-{DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()}";
            try
            {
                var authorized = await _payPal.AuthorizeCardPaymentAsync(new PayPalAuthorizeRequest
                {
                    RequestId = $"eshop-pay-{order.Id}-{DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()}",
                    Amount = amount,
                    Currency = payment.Currency,
                    InvoiceId = invoiceId,
                    CustomId = order.Id.ToString(CultureInfo.InvariantCulture),
                    Description = $"eShopOnWeb order {order.Id}",
                    Card = card,
                    VaultId = vaultId
                }, cancellationToken);

                payment.RecordPayPalOrder(authorized.OrderId, authorized.OrderStatus, invoiceId);
                payment.RecordAuthorization(
                    authorized.Authorization.Id,
                    authorized.Authorization.Status,
                    authorized.Authorization.CreateTime,
                    authorized.Authorization.ExpirationTime);
                order.MarkAuthorized();

                await _paymentRepository.UpdateAsync(payment, cancellationToken);
                await _orderRepository.UpdateAsync(order, cancellationToken);
                return Map(order, payment);
            }
            catch (PaymentException ex) when (!string.IsNullOrEmpty(ex.Issue) || (int)ex.StatusCode >= 400)
            {
                throw WrapPayPal(ex, "PayPal could not authorize this payment.");
            }
        });
    }

    public async Task<OrderDetailsDto> FulfilAsync(int orderId, CancellationToken cancellationToken = default)
    {
        return await WithOrderLock(orderId, async () =>
        {
            var (order, payment) = await LoadOrderAsync(orderId, cancellationToken);

            if (order.Status is OrderStatus.Fulfilled or OrderStatus.PartiallyRefunded or OrderStatus.Refunded
                && !string.IsNullOrEmpty(payment.PayPalCaptureId))
            {
                return Map(order, payment);
            }

            if (order.Status == OrderStatus.Cancelled)
            {
                throw new PaymentException("A cancelled order cannot be fulfilled.", HttpStatusCode.Conflict);
            }

            if (order.Status != OrderStatus.Authorized || string.IsNullOrEmpty(payment.PayPalAuthorizationId))
            {
                throw new PaymentException("The order must be authorized before it can be fulfilled.", HttpStatusCode.Conflict);
            }

            try
            {
                var authorizationId = await EnsureCapturableAuthorizationAsync(payment, cancellationToken);

                var capture = await _payPal.CaptureAuthorizationAsync(
                    authorizationId,
                    payment.Amount,
                    payment.Currency,
                    $"eshop-capture-{authorizationId}",
                    cancellationToken);

                payment.RecordCapture(
                    capture.Id,
                    capture.Status,
                    capture.Amount,
                    capture.PayPalFee,
                    capture.NetAmount,
                    capture.CreateTime);
                order.MarkFulfilled();

                await _paymentRepository.UpdateAsync(payment, cancellationToken);
                await _orderRepository.UpdateAsync(order, cancellationToken);
                return Map(order, payment);
            }
            catch (PaymentException ex)
            {
                if (string.Equals(ex.Issue, "AUTHORIZATION_EXPIRED", StringComparison.OrdinalIgnoreCase)
                    || string.Equals(ex.Issue, "AUTHORIZATION_VOIDED", StringComparison.OrdinalIgnoreCase)
                    || string.Equals(ex.Issue, "MAX_NUMBER_OF_REAUTHORIZATION_ATTEMPTS_REACHED", StringComparison.OrdinalIgnoreCase)
                    || string.Equals(ex.Issue, "REAUTHORIZATION", StringComparison.OrdinalIgnoreCase))
                {
                    throw CannotRenew(ex);
                }

                throw WrapPayPal(ex, "PayPal could not capture this payment.");
            }
        });
    }

    public async Task<OrderDetailsDto> CancelAsync(int orderId, CancellationToken cancellationToken = default)
    {
        return await WithOrderLock(orderId, async () =>
        {
            var (order, payment) = await LoadOrderAsync(orderId, cancellationToken);

            if (order.Status == OrderStatus.Cancelled)
            {
                return Map(order, payment);
            }

            if (order.Status is OrderStatus.Fulfilled or OrderStatus.PartiallyRefunded or OrderStatus.Refunded)
            {
                throw new PaymentException("A fulfilled order cannot be cancelled. Issue a refund instead.", HttpStatusCode.Conflict);
            }

            if (order.Status == OrderStatus.Authorized && !string.IsNullOrEmpty(payment.PayPalAuthorizationId))
            {
                try
                {
                    await _payPal.VoidAuthorizationAsync(
                        payment.PayPalAuthorizationId,
                        $"eshop-void-{payment.PayPalAuthorizationId}",
                        cancellationToken);
                    payment.RecordVoid("VOIDED");
                    await _paymentRepository.UpdateAsync(payment, cancellationToken);
                }
                catch (PaymentException ex)
                {
                    throw WrapPayPal(ex, "PayPal could not release the authorization.");
                }
            }

            order.MarkCancelled();
            await _orderRepository.UpdateAsync(order, cancellationToken);
            return Map(order, payment);
        });
    }

    public async Task<RefundDetailsDto> RefundAsync(
        string buyerId,
        int orderId,
        RefundOrderCommand command,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(command.IdempotencyKey))
        {
            throw new PaymentException("An idempotencyKey is required for refunds.");
        }

        if (command.IdempotencyKey.Length > 108)
        {
            throw new PaymentException("idempotencyKey must be 108 characters or fewer.");
        }

        return await WithOrderLock(orderId, async () =>
        {
            var (order, payment) = await LoadOwnedOrderAsync(buyerId, orderId, cancellationToken);

            var existing = payment.FindRefundByIdempotencyKey(command.IdempotencyKey);
            if (existing != null)
            {
                return MapRefund(existing);
            }

            if (order.Status is not (OrderStatus.Fulfilled or OrderStatus.PartiallyRefunded)
                || string.IsNullOrEmpty(payment.PayPalCaptureId)
                || payment.CapturedAmount is null)
            {
                throw new PaymentException("Only a captured, fulfilled order can be refunded.", HttpStatusCode.Conflict);
            }

            if (order.Status == OrderStatus.Refunded || payment.RemainingRefundableAmount <= 0)
            {
                throw new PaymentException("This order has already been refunded in full.", HttpStatusCode.Conflict);
            }

            var amount = command.Amount.HasValue
                ? decimal.Round(command.Amount.Value, 2, MidpointRounding.AwayFromZero)
                : payment.RemainingRefundableAmount;

            if (amount <= 0)
            {
                throw new PaymentException("Refund amount must be greater than zero.");
            }

            if (ToCents(amount) > ToCents(payment.RemainingRefundableAmount))
            {
                throw new PaymentException(
                    $"Refund amount {amount.ToString("0.00", CultureInfo.InvariantCulture)} exceeds the remaining captured amount {payment.RemainingRefundableAmount.ToString("0.00", CultureInfo.InvariantCulture)}.");
            }

            try
            {
                var paypalRefund = await _payPal.RefundCaptureAsync(
                    payment.PayPalCaptureId,
                    amount,
                    payment.Currency,
                    BuildRefundRequestId(payment.PayPalCaptureId, command.IdempotencyKey),
                    cancellationToken);

                var refund = payment.RecordRefund(
                    paypalRefund.Id,
                    command.IdempotencyKey,
                    paypalRefund.Amount == 0 ? amount : paypalRefund.Amount,
                    paypalRefund.Status,
                    paypalRefund.CreateTime);

                if (payment.Status == OrderPaymentStatus.Refunded)
                {
                    order.MarkRefunded();
                }
                else
                {
                    order.MarkPartiallyRefunded();
                }

                await _paymentRepository.UpdateAsync(payment, cancellationToken);
                await _orderRepository.UpdateAsync(order, cancellationToken);
                return MapRefund(refund);
            }
            catch (PaymentException ex)
            {
                throw WrapPayPal(ex, "PayPal could not refund this payment.");
            }
        });
    }

    public async Task<IReadOnlyList<OrderDetailsDto>> ListMyOrdersAsync(
        string buyerId,
        CancellationToken cancellationToken = default)
    {
        var orders = await _orderRepository.ListAsync(new CustomerOrdersSpecification(buyerId), cancellationToken);
        var payments = await _paymentRepository.ListAsync(new OrderPaymentsWithRefundsSpec(), cancellationToken);
        var byOrderId = payments.Where(p => p.BuyerId == buyerId).ToDictionary(p => p.OrderId);

        return orders
            .OrderByDescending(o => o.OrderDate)
            .Select(order => Map(order, byOrderId.TryGetValue(order.Id, out var payment) ? payment : null))
            .ToList();
    }

    private async Task<string> EnsureCapturableAuthorizationAsync(OrderPayment payment, CancellationToken cancellationToken)
    {
        var authorizationId = payment.PayPalAuthorizationId
            ?? throw new PaymentException("No PayPal authorization is stored for this order.", HttpStatusCode.Conflict);

        PayPalAuthorization live;
        try
        {
            live = await _payPal.GetAuthorizationAsync(authorizationId, cancellationToken);
        }
        catch (PaymentException ex)
        {
            throw WrapPayPal(ex, "PayPal could not load the authorization for fulfilment.");
        }

        payment.RecordAuthorization(live.Id, live.Status, live.CreateTime ?? payment.AuthorizationCreatedAt, live.ExpirationTime);
        await _paymentRepository.UpdateAsync(payment, cancellationToken);

        if (string.Equals(live.Status, "CAPTURED", StringComparison.OrdinalIgnoreCase)
            && !string.IsNullOrEmpty(payment.PayPalCaptureId))
        {
            return live.Id;
        }

        if (string.Equals(live.Status, "VOIDED", StringComparison.OrdinalIgnoreCase)
            || string.Equals(live.Status, "DENIED", StringComparison.OrdinalIgnoreCase)
            || string.Equals(live.Status, "EXPIRED", StringComparison.OrdinalIgnoreCase))
        {
            throw new PaymentException(
                $"The PayPal authorization is '{live.Status}' and can no longer be captured or renewed. Ask the shopper to pay the order again so a new hold can be placed, then retry fulfilment.",
                HttpStatusCode.Conflict);
        }

        var created = live.CreateTime ?? payment.AuthorizationCreatedAt ?? DateTimeOffset.UtcNow;
        var expiresAt = live.ExpirationTime ?? created.Add(MaxAuthorizationLifetime);
        var now = DateTimeOffset.UtcNow;

        if (now >= expiresAt)
        {
            throw new PaymentException(
                "The PayPal authorization has expired and can no longer be renewed. Ask the shopper to pay the order again so a new hold can be placed, then retry fulfilment.",
                HttpStatusCode.Conflict);
        }

        if (now >= created.Add(HonorPeriod))
        {
            try
            {
                var reauthorized = await _payPal.ReauthorizeAsync(
                    live.Id,
                    payment.Amount,
                    payment.Currency,
                    $"eshop-reauth-{live.Id}",
                    cancellationToken);

                payment.RecordAuthorization(
                    reauthorized.Id,
                    reauthorized.Status,
                    reauthorized.CreateTime,
                    reauthorized.ExpirationTime);
                await _paymentRepository.UpdateAsync(payment, cancellationToken);
                _logger.LogInformation(
                    "Renewed stale PayPal authorization for order {OrderId}; new authorization {AuthorizationId}.",
                    payment.OrderId,
                    reauthorized.Id);
                return reauthorized.Id;
            }
            catch (PaymentException ex)
            {
                throw CannotRenew(ex);
            }
        }

        return live.Id;
    }

    private async Task<(Order Order, OrderPayment Payment)> LoadOwnedOrderAsync(
        string buyerId,
        int orderId,
        CancellationToken cancellationToken)
    {
        var (order, payment) = await LoadOrderAsync(orderId, cancellationToken);
        if (!string.Equals(order.BuyerId, buyerId, StringComparison.Ordinal))
        {
            throw new PaymentException("Order was not found.", HttpStatusCode.NotFound);
        }

        return (order, payment);
    }

    private async Task<(Order Order, OrderPayment Payment)> LoadOrderAsync(int orderId, CancellationToken cancellationToken)
    {
        var order = await _orderRepository.FirstOrDefaultAsync(new OrderWithItemsByIdSpec(orderId), cancellationToken)
            ?? throw new PaymentException("Order was not found.", HttpStatusCode.NotFound);

        var payment = await _paymentRepository.FirstOrDefaultAsync(new OrderPaymentByOrderIdSpec(orderId), cancellationToken)
            ?? throw new PaymentException("Order payment was not found.", HttpStatusCode.NotFound);

        return (order, payment);
    }

    private static async Task<T> WithOrderLock<T>(int orderId, Func<Task<T>> action)
    {
        var gate = OrderGates.GetOrAdd(orderId, _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync();
        try
        {
            return await action();
        }
        finally
        {
            gate.Release();
        }
    }

    private static PayPalCardDetails ToPayPalCard(CardPaymentCommand card)
    {
        if (string.IsNullOrWhiteSpace(card.Number) || string.IsNullOrWhiteSpace(card.Expiry))
        {
            throw new PaymentException("Card number and expiry are required.");
        }

        return new PayPalCardDetails
        {
            Number = card.Number.Replace(" ", string.Empty, StringComparison.Ordinal),
            Expiry = card.Expiry,
            SecurityCode = card.SecurityCode,
            Name = card.Name,
            BillingAddress = card.BillingAddress == null
                ? null
                : new PayPalBillingAddress
                {
                    AddressLine1 = card.BillingAddress.AddressLine1,
                    AddressLine2 = card.BillingAddress.AddressLine2,
                    AdminArea1 = card.BillingAddress.AdminArea1,
                    AdminArea2 = card.BillingAddress.AdminArea2,
                    PostalCode = card.BillingAddress.PostalCode,
                    CountryCode = card.BillingAddress.CountryCode
                }
        };
    }

    private static OrderDetailsDto Map(Order order, OrderPayment? payment)
    {
        return new OrderDetailsDto
        {
            OrderId = order.Id,
            BuyerId = order.BuyerId,
            Status = order.Status.ToString(),
            Total = order.Total(),
            Currency = payment?.Currency ?? string.Empty,
            OrderDate = order.OrderDate,
            Items = order.OrderItems.Select(i => new OrderItemDetailsDto
            {
                CatalogItemId = i.ItemOrdered.CatalogItemId,
                ProductName = i.ItemOrdered.ProductName,
                UnitPrice = i.UnitPrice,
                Quantity = i.Units
            }).ToList(),
            Payment = payment == null ? null : new PaymentDetailsDto
            {
                Status = payment.Status.ToString(),
                Amount = payment.Amount,
                Currency = payment.Currency,
                PayPalOrderId = payment.PayPalOrderId,
                PayPalOrderStatus = payment.PayPalOrderStatus,
                InvoiceId = payment.InvoiceId,
                PayPalAuthorizationId = payment.PayPalAuthorizationId,
                AuthorizationStatus = payment.AuthorizationStatus,
                AuthorizationCreatedAt = payment.AuthorizationCreatedAt,
                AuthorizationExpiresAt = payment.AuthorizationExpiresAt,
                PayPalCaptureId = payment.PayPalCaptureId,
                CaptureStatus = payment.CaptureStatus,
                CapturedAmount = payment.CapturedAmount,
                PayPalFee = payment.PayPalFee,
                NetAmount = payment.NetAmount,
                CapturedAt = payment.CapturedAt,
                RefundedAmount = payment.RefundedAmount,
                RemainingRefundableAmount = payment.RemainingRefundableAmount,
                Refunds = payment.Refunds.Select(MapRefund).ToList()
            }
        };
    }

    private static RefundDetailsDto MapRefund(PaymentRefund refund)
    {
        return new RefundDetailsDto
        {
            RefundId = refund.Id,
            PayPalRefundId = refund.PayPalRefundId,
            Amount = refund.Amount,
            Currency = refund.Currency,
            Status = refund.Status,
            CreatedAt = refund.CreatedAt
        };
    }

    private static PaymentException WrapPayPal(PaymentException ex, string fallback)
    {
        var status = ex.StatusCode switch
        {
            HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden => HttpStatusCode.BadGateway,
            HttpStatusCode.NotFound => HttpStatusCode.Conflict,
            HttpStatusCode.Conflict => HttpStatusCode.Conflict,
            HttpStatusCode.UnprocessableEntity => HttpStatusCode.UnprocessableEntity,
            HttpStatusCode.BadRequest => HttpStatusCode.BadRequest,
            _ when (int)ex.StatusCode >= 500 => HttpStatusCode.BadGateway,
            _ => HttpStatusCode.BadRequest
        };

        return new PaymentException(string.IsNullOrWhiteSpace(ex.Message) ? fallback : ex.Message, status, ex.Issue);
    }

    private static PaymentException CannotRenew(PaymentException ex)
    {
        return new PaymentException(
            "The PayPal authorization can no longer be renewed. Ask the shopper to pay the order again so a new hold can be placed, then retry fulfilment. "
            + ex.Message,
            HttpStatusCode.Conflict,
            ex.Issue);
    }

    private static string BuildRefundRequestId(string captureId, string idempotencyKey)
    {
        var raw = $"rf-{captureId}-{idempotencyKey}";
        return raw.Length <= 108 ? raw : raw[..108];
    }

    private static long ToCents(decimal amount) =>
        (long)Math.Round(amount * 100m, MidpointRounding.AwayFromZero);
}
