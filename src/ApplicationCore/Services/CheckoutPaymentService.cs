using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.SavedPaymentMethodAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Payments;
using Microsoft.eShopWeb.ApplicationCore.Specifications;

namespace Microsoft.eShopWeb.ApplicationCore.Services;

public class CheckoutPaymentService : ICheckoutPaymentService
{
    private static readonly TimeSpan HonorPeriod = TimeSpan.FromDays(3);
    private static readonly TimeSpan MaxAuthorizationLifetime = TimeSpan.FromDays(29);

    private readonly IRepository<Order> _orderRepository;
    private readonly IRepository<OrderPayment> _paymentRepository;
    private readonly IRepository<CatalogItem> _catalogRepository;
    private readonly IRepository<SavedPaymentMethod> _savedPaymentMethodRepository;
    private readonly IPayPalClient _payPalClient;
    private readonly IUriComposer _uriComposer;
    private readonly PaymentOperationGate _gate;
    private readonly IPaymentSettings _paymentSettings;

    public CheckoutPaymentService(
        IRepository<Order> orderRepository,
        IRepository<OrderPayment> paymentRepository,
        IRepository<CatalogItem> catalogRepository,
        IRepository<SavedPaymentMethod> savedPaymentMethodRepository,
        IPayPalClient payPalClient,
        IUriComposer uriComposer,
        PaymentOperationGate gate,
        IPaymentSettings paymentSettings)
    {
        _orderRepository = orderRepository;
        _paymentRepository = paymentRepository;
        _catalogRepository = catalogRepository;
        _savedPaymentMethodRepository = savedPaymentMethodRepository;
        _payPalClient = payPalClient;
        _uriComposer = uriComposer;
        _gate = gate;
        _paymentSettings = paymentSettings;
    }

    public async Task<Order> PlaceOrderAsync(PlaceOrderRequest request, CancellationToken cancellationToken = default)
    {
        if (request.Items is null || request.Items.Count == 0)
        {
            throw new PaymentException("At least one catalog item is required.", 400);
        }

        var ids = request.Items.Select(i => i.CatalogItemId).Distinct().ToArray();
        var catalogItems = await _catalogRepository.ListAsync(new CatalogItemsSpecification(ids), cancellationToken);
        var byId = catalogItems.ToDictionary(c => c.Id);

        var orderItems = new List<OrderItem>();
        foreach (var line in request.Items)
        {
            if (line.Quantity <= 0)
            {
                throw new PaymentException("Item quantity must be greater than zero.", 400);
            }

            if (!byId.TryGetValue(line.CatalogItemId, out var catalogItem))
            {
                throw new PaymentException($"Catalog item {line.CatalogItemId} was not found.", 404);
            }

            var itemOrdered = new CatalogItemOrdered(
                catalogItem.Id,
                catalogItem.Name,
                _uriComposer.ComposePicUri(catalogItem.PictureUri));
            orderItems.Add(new OrderItem(itemOrdered, catalogItem.Price, line.Quantity));
        }

        var shipTo = request.ShipTo ?? new Address("123 Main St.", "Kent", "OH", "US", "44240");
        var order = new Order(request.BuyerId, shipTo, orderItems, OrderStatus.AwaitingPayment);
        await _orderRepository.AddAsync(order, cancellationToken);
        return order;
    }

    public Task<Order> PayOrderAsync(PayOrderRequest request, CancellationToken cancellationToken = default) =>
        _gate.RunAsync($"order:{request.OrderId}", () => PayOrderCoreAsync(request, cancellationToken));

    private async Task<Order> PayOrderCoreAsync(PayOrderRequest request, CancellationToken cancellationToken)
    {
        var order = await GetOrderAsync(request.OrderId, cancellationToken);
        EnsureBuyer(order, request.BuyerId);

        if (order.Status is OrderStatus.Authorized or OrderStatus.Fulfilled or OrderStatus.PartiallyRefunded or OrderStatus.Refunded)
        {
            return order;
        }

        if (order.Status is OrderStatus.Cancelled)
        {
            throw new PaymentException("A cancelled order cannot be paid.", 409);
        }

        if (order.Status is not OrderStatus.AwaitingPayment and not OrderStatus.Placed)
        {
            throw new PaymentException($"Order {order.Id} cannot be paid from status {order.Status}.", 409);
        }

        var currency = _paymentSettings.Currency;
        var amount = MoneyFormat.ToPayPalValue(order.Total(), currency);
        var invoiceId = $"ESHOP-{order.Id}-{Guid.NewGuid():N}";
        var customId = order.Id.ToString();

        var payment = order.Payment ?? await GetOrCreatePaymentAsync(order, currency, invoiceId, cancellationToken);

        if (!string.IsNullOrWhiteSpace(payment.AuthorizationId) &&
            !string.Equals(payment.AuthorizationStatus, "VOIDED", StringComparison.OrdinalIgnoreCase))
        {
            order.MarkAuthorized();
            await _orderRepository.UpdateAsync(order, cancellationToken);
            return order;
        }

        string? vaultId = null;
        CardPaymentSource? card = request.Card;
        if (request.PaymentMethodId is int paymentMethodId)
        {
            var saved = await _savedPaymentMethodRepository.GetByIdAsync(paymentMethodId, cancellationToken)
                        ?? throw new PaymentException("Saved payment method was not found.", 404);
            if (!string.Equals(saved.BuyerId, request.BuyerId, StringComparison.Ordinal))
            {
                throw new PaymentException("Saved payment method was not found.", 404);
            }

            vaultId = saved.PayPalPaymentTokenId;
            card = null;
        }
        else if (card is null)
        {
            throw new PaymentException("Provide card details or a saved payment method id.", 400);
        }

        ValidateCardIfPresent(card);
        card = NormalizeCard(card);

        var create = await _payPalClient.CreateAuthorizeOrderAsync(
            new CreatePayPalAuthorizeRequest
            {
                InvoiceId = payment.InvoiceId,
                CustomId = customId,
                CurrencyCode = currency,
                AmountValue = amount,
                Card = card,
                VaultId = vaultId
            },
            paypalRequestId: $"eshop-create-{order.Id}-{payment.InvoiceId}",
            cancellationToken);

        payment.RecordPayPalOrder(create.Id, create.Status);
        await _paymentRepository.UpdateAsync(payment, cancellationToken);

        EnsureNoPayerAction(create);

        var authorized = create;
        if (authorized.Authorization is null &&
            !string.Equals(create.Status, "COMPLETED", StringComparison.OrdinalIgnoreCase))
        {
            authorized = await _payPalClient.AuthorizeOrderAsync(
                create.Id,
                paypalRequestId: $"eshop-auth-{order.Id}-{payment.InvoiceId}",
                cancellationToken);
            payment.RecordPayPalOrder(authorized.Id, authorized.Status);
            EnsureNoPayerAction(authorized);
        }

        var authorization = authorized.Authorization
                            ?? throw new PaymentException("PayPal did not return an authorization for this payment.", 502);

        if (!string.Equals(authorization.Amount.Value, amount, StringComparison.Ordinal) ||
            !string.Equals(authorization.Amount.CurrencyCode, currency, StringComparison.OrdinalIgnoreCase))
        {
            throw new PaymentException(
                $"PayPal authorized {authorization.Amount.CurrencyCode} {authorization.Amount.Value} but the order total is {currency} {amount}.",
                502);
        }

        payment.RecordAuthorization(
            authorization.Id,
            authorization.Status,
            authorization.Amount.ToDecimal(),
            authorization.CreateTime ?? DateTimeOffset.UtcNow,
            authorization.ExpirationTime);
        await _paymentRepository.UpdateAsync(payment, cancellationToken);

        order.AttachPayment(payment);
        order.MarkAuthorized();
        await _orderRepository.UpdateAsync(order, cancellationToken);
        return order;
    }

    public Task<Order> FulfilOrderAsync(int orderId, CancellationToken cancellationToken = default) =>
        _gate.RunAsync($"order:{orderId}", () => FulfilOrderCoreAsync(orderId, cancellationToken));

    private async Task<Order> FulfilOrderCoreAsync(int orderId, CancellationToken cancellationToken)
    {
        var order = await GetOrderAsync(orderId, cancellationToken);
        if (order.Status is OrderStatus.Fulfilled or OrderStatus.PartiallyRefunded or OrderStatus.Refunded)
        {
            return order;
        }

        if (order.Status is OrderStatus.Cancelled)
        {
            throw new PaymentException("A cancelled order cannot be fulfilled.", 409);
        }

        if (order.Status is not OrderStatus.Authorized || order.Payment is null ||
            string.IsNullOrWhiteSpace(order.Payment.AuthorizationId))
        {
            throw new PaymentException("Order must be authorized before it can be fulfilled.", 409);
        }

        var payment = order.Payment;
        var authorizationId = await EnsureFreshAuthorizationAsync(payment, cancellationToken);

        var capture = await _payPalClient.CaptureAuthorizationAsync(
            authorizationId,
            paypalRequestId: $"eshop-capture-{order.Id}-{payment.InvoiceId}",
            cancellationToken);

        if (capture.PayPalFee is null || capture.NetAmount is null)
        {
            try
            {
                var refreshed = await _payPalClient.GetCaptureAsync(capture.Id, cancellationToken);
                if (string.Equals(refreshed.Amount.Value, capture.Amount.Value, StringComparison.Ordinal) &&
                    (refreshed.PayPalFee is not null || refreshed.NetAmount is not null))
                {
                    capture = refreshed;
                }
            }
            catch (PaymentException)
            {
                // Keep the original capture snapshot if the follow-up read fails.
            }
        }

        var expectedCapture = MoneyFormat.ToPayPalValue(payment.AuthorizedAmount, payment.Currency);
        if (!string.Equals(capture.Amount.Value, expectedCapture, StringComparison.Ordinal))
        {
            throw new PaymentException(
                $"PayPal captured {capture.Amount.CurrencyCode} {capture.Amount.Value} but the authorization was {payment.Currency} {expectedCapture}.",
                502);
        }

        payment.RecordCapture(
            capture.Id,
            capture.Status,
            capture.Amount.ToDecimal(),
            capture.PayPalFee?.ToDecimal(),
            capture.NetAmount?.ToDecimal(),
            capture.CreateTime ?? DateTimeOffset.UtcNow);
        await _paymentRepository.UpdateAsync(payment, cancellationToken);

        order.MarkFulfilled();
        await _orderRepository.UpdateAsync(order, cancellationToken);
        return order;
    }

    public Task<Order> CancelOrderAsync(int orderId, CancellationToken cancellationToken = default) =>
        _gate.RunAsync($"order:{orderId}", () => CancelOrderCoreAsync(orderId, cancellationToken));

    private async Task<Order> CancelOrderCoreAsync(int orderId, CancellationToken cancellationToken)
    {
        var order = await GetOrderAsync(orderId, cancellationToken);
        if (order.Status is OrderStatus.Cancelled)
        {
            return order;
        }

        if (order.Status is OrderStatus.Fulfilled or OrderStatus.PartiallyRefunded or OrderStatus.Refunded)
        {
            throw new PaymentException("A fulfilled order cannot be cancelled; issue a refund instead.", 409);
        }

        if (order.Payment?.AuthorizationId is { Length: > 0 } authorizationId &&
            !string.Equals(order.Payment.AuthorizationStatus, "VOIDED", StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(order.Payment.AuthorizationStatus, "CAPTURED", StringComparison.OrdinalIgnoreCase))
        {
            await _payPalClient.VoidAuthorizationAsync(
                authorizationId,
                paypalRequestId: $"eshop-void-{order.Id}-{order.Payment.InvoiceId}",
                cancellationToken);
            order.Payment.RecordVoid("VOIDED");
            await _paymentRepository.UpdateAsync(order.Payment, cancellationToken);
        }

        order.MarkCancelled();
        await _orderRepository.UpdateAsync(order, cancellationToken);
        return order;
    }

    public Task<(Order Order, OrderRefund Refund)> RefundOrderAsync(
        RefundOrderRequest request,
        CancellationToken cancellationToken = default) =>
        _gate.RunAsync($"order:{request.OrderId}", () => RefundOrderCoreAsync(request, cancellationToken));

    private async Task<(Order Order, OrderRefund Refund)> RefundOrderCoreAsync(
        RefundOrderRequest request,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.IdempotencyKey))
        {
            throw new PaymentException("An idempotency key is required to refund.", 400);
        }

        if (request.IdempotencyKey.Length > 108)
        {
            throw new PaymentException("The refund idempotency key must be 108 characters or fewer.", 400);
        }

        var order = await GetOrderAsync(request.OrderId, cancellationToken);
        EnsureBuyer(order, request.BuyerId);

        if (order.Status is not OrderStatus.Fulfilled and not OrderStatus.PartiallyRefunded and not OrderStatus.Refunded)
        {
            throw new PaymentException("Only a fulfilled order can be refunded.", 409);
        }

        var payment = order.Payment ?? throw new PaymentException("Order has no captured payment to refund.", 409);
        if (string.IsNullOrWhiteSpace(payment.CaptureId) || payment.CapturedAmount is null)
        {
            throw new PaymentException("Order has no captured payment to refund.", 409);
        }

        var existing = payment.FindRefundByIdempotencyKey(request.IdempotencyKey);
        if (existing is not null)
        {
            return (order, existing);
        }

        var remaining = payment.RefundableRemaining;
        if (remaining <= 0)
        {
            throw new PaymentException("This capture has already been refunded in full.", 409);
        }

        decimal refundAmount;
        string? amountValue;
        if (request.Amount is decimal requested)
        {
            refundAmount = MoneyFormat.Round(requested, payment.Currency);
            if (refundAmount <= 0)
            {
                throw new PaymentException("Refund amount must be greater than zero.", 400);
            }

            if (refundAmount > remaining)
            {
                throw new PaymentException(
                    $"Refund of {refundAmount} exceeds the remaining captured amount of {remaining}.",
                    409);
            }

            amountValue = MoneyFormat.ToPayPalValue(refundAmount, payment.Currency);
        }
        else
        {
            refundAmount = remaining;
            amountValue = remaining == payment.CapturedAmount
                ? null
                : MoneyFormat.ToPayPalValue(refundAmount, payment.Currency);
        }

        var paypalRequestId = $"eshop-rf-{payment.CaptureId}-{request.IdempotencyKey}";
        if (paypalRequestId.Length > 108)
        {
            paypalRequestId = paypalRequestId[..108];
        }

        var snapshot = await _payPalClient.RefundCaptureAsync(
            payment.CaptureId,
            amountValue is null ? null : payment.Currency,
            amountValue,
            paypalRequestId,
            cancellationToken);

        var refund = payment.AddRefund(snapshot.Id, snapshot.Status, snapshot.Amount.ToDecimal(), request.IdempotencyKey);
        await _paymentRepository.UpdateAsync(payment, cancellationToken);

        order.MarkRefunded(partial: payment.RefundableRemaining > 0);
        await _orderRepository.UpdateAsync(order, cancellationToken);
        return (order, refund);
    }

    public async Task<IReadOnlyList<Order>> ListMyOrdersAsync(string buyerId, CancellationToken cancellationToken = default)
    {
        return await _orderRepository.ListAsync(new CustomerOrdersWithPaymentSpecification(buyerId), cancellationToken);
    }

    public async Task<Order> GetOrderForBuyerAsync(int orderId, string buyerId, CancellationToken cancellationToken = default)
    {
        var order = await GetOrderAsync(orderId, cancellationToken);
        EnsureBuyer(order, buyerId);
        return order;
    }

    private async Task<string> EnsureFreshAuthorizationAsync(OrderPayment payment, CancellationToken cancellationToken)
    {
        var authorizationId = payment.AuthorizationId
                              ?? throw new PaymentException("Order has no PayPal authorization to capture.", 409);

        PayPalAuthorizationSnapshot? live = null;
        try
        {
            live = await _payPalClient.GetAuthorizationAsync(authorizationId, cancellationToken);
            payment.RecordReauthorization(live.Id, live.Status, live.CreateTime ?? DateTimeOffset.UtcNow, live.ExpirationTime);
        }
        catch (PaymentException)
        {
            // Fall through and attempt capture/reauthorize based on locally stored timestamps.
        }

        var now = DateTimeOffset.UtcNow;
        var original = payment.OriginalAuthorizationAt ?? payment.AuthorizationCreatedAt ?? now;
        var expiresAt = live?.ExpirationTime ?? payment.AuthorizationExpiresAt;
        var honorEnds = (payment.AuthorizationCreatedAt ?? original) + HonorPeriod;
        var stale = (expiresAt is not null && expiresAt <= now) || now >= honorEnds;

        if (!stale)
        {
            return payment.AuthorizationId!;
        }

        if (now - original >= MaxAuthorizationLifetime)
        {
            throw new PaymentException(
                "The PayPal authorization can no longer be renewed (more than 29 days have passed). Cancel the order and ask the shopper to pay again.",
                409);
        }

        try
        {
            var reauthorized = await _payPalClient.ReauthorizeAsync(
                payment.AuthorizationId!,
                payment.Currency,
                MoneyFormat.ToPayPalValue(payment.AuthorizedAmount, payment.Currency),
                paypalRequestId: $"eshop-reauth-{payment.OrderId}-{payment.InvoiceId}",
                cancellationToken);

            payment.RecordReauthorization(
                reauthorized.Id,
                reauthorized.Status,
                reauthorized.CreateTime ?? DateTimeOffset.UtcNow,
                reauthorized.ExpirationTime);
            await _paymentRepository.UpdateAsync(payment, cancellationToken);
            return reauthorized.Id;
        }
        catch (PaymentException ex)
        {
            throw new PaymentException(
                "The PayPal authorization is stale and could not be renewed. Cancel the order and ask the shopper to pay again. " + ex.Message,
                409);
        }
    }

    private async Task<Order> GetOrderAsync(int orderId, CancellationToken cancellationToken)
    {
        var order = await _orderRepository.FirstOrDefaultAsync(new OrderWithPaymentByIdSpec(orderId), cancellationToken);
        if (order is null)
        {
            throw new PaymentException($"Order {orderId} was not found.", 404);
        }

        return order;
    }

    private async Task<OrderPayment> GetOrCreatePaymentAsync(
        Order order,
        string currency,
        string invoiceId,
        CancellationToken cancellationToken)
    {
        if (order.Payment is not null)
        {
            return order.Payment;
        }

        var existing = await _paymentRepository.FirstOrDefaultAsync(new OrderPaymentByOrderIdSpec(order.Id), cancellationToken);
        if (existing is not null)
        {
            order.AttachPayment(existing);
            return existing;
        }

        var payment = new OrderPayment(order.Id, currency, invoiceId);
        await _paymentRepository.AddAsync(payment, cancellationToken);
        order.AttachPayment(payment);
        await _orderRepository.UpdateAsync(order, cancellationToken);
        return payment;
    }

    private static void EnsureBuyer(Order order, string buyerId)
    {
        if (!string.Equals(order.BuyerId, buyerId, StringComparison.Ordinal))
        {
            throw new PaymentException($"Order {order.Id} was not found.", 404);
        }
    }

    private static void EnsureNoPayerAction(PayPalOrderSnapshot snapshot)
    {
        if (string.Equals(snapshot.Status, "PAYER_ACTION_REQUIRED", StringComparison.OrdinalIgnoreCase) ||
            snapshot.PayerActionLinks.Count > 0)
        {
            throw new PayerActionRequiredException(snapshot.Id);
        }
    }

    private static void ValidateCardIfPresent(CardPaymentSource? card)
    {
        if (card is null)
        {
            return;
        }

        var number = new string(card.Number.Where(char.IsDigit).ToArray());
        if (number.Length is < 13 or > 19)
        {
            throw new PaymentException("Card number is invalid.", 400);
        }

        if (card.Expiry is not { Length: 7 } || card.Expiry[4] != '-')
        {
            throw new PaymentException("Card expiry must be in YYYY-MM format.", 400);
        }

        if (card.SecurityCode is not { Length: >= 3 and <= 4 })
        {
            throw new PaymentException("Card security code is invalid.", 400);
        }
    }

    private static CardPaymentSource? NormalizeCard(CardPaymentSource? card)
    {
        if (card is null)
        {
            return null;
        }

        return new CardPaymentSource
        {
            Number = new string(card.Number.Where(char.IsDigit).ToArray()),
            Expiry = card.Expiry,
            SecurityCode = card.SecurityCode.Trim(),
            Name = card.Name,
            BillingAddress = card.BillingAddress
        };
    }
}
