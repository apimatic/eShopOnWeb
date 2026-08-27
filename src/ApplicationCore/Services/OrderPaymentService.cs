using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.SavedCardAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Specifications;

namespace Microsoft.eShopWeb.ApplicationCore.Services;

public class OrderPaymentService : IOrderPaymentService
{
    // A fresh instance per order — EF owned entities cannot share one instance.
    private static Address DefaultShipTo() =>
        new Address("Main Street", "Seattle", "WA", "United States", "98101");

    private readonly IRepository<Order> _orderRepository;
    private readonly IRepository<CatalogItem> _itemRepository;
    private readonly IRepository<SavedCard> _savedCardRepository;
    private readonly IPaymentGateway _paymentGateway;
    private readonly IUriComposer _uriComposer;

    public OrderPaymentService(
        IRepository<Order> orderRepository,
        IRepository<CatalogItem> itemRepository,
        IRepository<SavedCard> savedCardRepository,
        IPaymentGateway paymentGateway,
        IUriComposer uriComposer)
    {
        _orderRepository = orderRepository;
        _itemRepository = itemRepository;
        _savedCardRepository = savedCardRepository;
        _paymentGateway = paymentGateway;
        _uriComposer = uriComposer;
    }

    public async Task<Order> CreateOrderAsync(string buyerId, IReadOnlyList<OrderItemRequest> items, CancellationToken ct)
    {
        if (items == null || items.Count == 0)
        {
            throw new PaymentDomainException("An order needs at least one item.", 400);
        }
        if (items.Any(i => i.Quantity <= 0))
        {
            throw new PaymentDomainException("Item quantities must be at least 1.", 400);
        }

        var spec = new CatalogItemsSpecification(items.Select(i => i.CatalogItemId).ToArray());
        var catalogItems = await _itemRepository.ListAsync(spec, ct);

        var missing = items.Select(i => i.CatalogItemId).Distinct()
            .Except(catalogItems.Select(c => c.Id)).ToList();
        if (missing.Count > 0)
        {
            throw new PaymentDomainException($"Unknown catalog item id(s): {string.Join(", ", missing)}.", 400);
        }

        var orderItems = items.Select(i =>
        {
            var catalogItem = catalogItems.First(c => c.Id == i.CatalogItemId);
            var itemOrdered = new CatalogItemOrdered(catalogItem.Id, catalogItem.Name,
                _uriComposer.ComposePicUri(catalogItem.PictureUri));
            return new OrderItem(itemOrdered, catalogItem.Price, i.Quantity);
        }).ToList();

        var order = new Order(buyerId, DefaultShipTo(), orderItems);
        await _orderRepository.AddAsync(order, ct);
        return order;
    }

    public async Task<Order> PayOrderAsync(string buyerId, int orderId, CardDetails? card, int? savedCardId, CancellationToken ct)
    {
        var order = await GetOrderAsync(orderId, ct);
        if (order.BuyerId != buyerId)
        {
            throw new PaymentDomainException($"Order {orderId} was not found.", 404);
        }

        // Idempotent in effect: a repeated pay on an authorized order returns current state.
        if (order.Status == OrderStatus.PaymentAuthorized && order.Payment?.AuthorizationId != null)
        {
            return order;
        }
        if (order.Status != OrderStatus.AwaitingPayment)
        {
            throw new PaymentDomainException($"Order {orderId} is {order.Status} and cannot be paid.");
        }
        if ((card == null) == (savedCardId == null))
        {
            throw new PaymentDomainException("Provide exactly one of 'card' or 'paymentMethodId'.", 400);
        }

        string? vaultTokenId = null;
        if (savedCardId != null)
        {
            var savedCard = await _savedCardRepository.GetByIdAsync(savedCardId.Value, ct);
            if (savedCard == null || savedCard.BuyerId != buyerId)
            {
                throw new PaymentDomainException($"Saved card {savedCardId} was not found.", 404);
            }
            vaultTokenId = savedCard.VaultTokenId;
        }

        var amount = order.Total();
        var payment = order.Payment;
        if (payment == null)
        {
            payment = new Payment(order.Id, amount, _paymentGateway.Currency, AuthorizeKey(order.Id, 1));
            payment.SetInvoiceId(InvoiceId(order.Id, 1));
            order.AttachPayment(payment);
        }
        else
        {
            var attempt = payment.AuthorizationAttempt + 1;
            payment.NextAuthorizationAttempt(AuthorizeKey(order.Id, attempt), InvoiceId(order.Id, attempt));
        }
        // Persist the idempotency key before the call so a retry reuses it.
        await _orderRepository.UpdateAsync(order, ct);

        var invoiceId = payment.InvoiceId!;
        AuthorizationResult authorization = vaultTokenId != null
            ? await _paymentGateway.AuthorizeWithVaultedCardAsync(amount, vaultTokenId,
                payment.AuthorizeIdempotencyKey, invoiceId, ct)
            : await _paymentGateway.AuthorizeWithCardAsync(amount, card!,
                payment.AuthorizeIdempotencyKey, invoiceId, ct);

        payment.SetAuthorization(authorization.PayPalOrderId, authorization.AuthorizationId,
            authorization.Status, authorization.ExpiresAt, vaultTokenId);
        order.MarkPaymentAuthorized();
        await _orderRepository.UpdateAsync(order, ct);
        return order;
    }

    public async Task<Order> FulfilOrderAsync(int orderId, CancellationToken ct)
    {
        var order = await GetOrderAsync(orderId, ct);

        if (order.Status == OrderStatus.Fulfilled)
        {
            return order;
        }
        var payment = order.Payment;
        if (order.Status != OrderStatus.PaymentAuthorized || payment?.AuthorizationId == null)
        {
            throw new PaymentDomainException($"Order {orderId} is {order.Status}; only a paid (authorized) order can be fulfilled.");
        }

        var state = await _paymentGateway.GetAuthorizationAsync(payment.AuthorizationId, ct);
        payment.SetAuthorizationStatus(state.Status, state.ExpiresAt);

        if (!IsCapturable(state))
        {
            try
            {
                state = await _paymentGateway.ReauthorizeAsync(payment.AuthorizationId,
                    payment.AuthorizedAmount, ReauthorizeKey(order.Id, payment.AuthorizationAttempt), ct);
                payment.SetAuthorizationStatus(state.Status, state.ExpiresAt);
            }
            catch (PaymentGatewayException)
            {
                payment.ClearAuthorization();
                order.ResetToAwaitingPayment();
                await _orderRepository.UpdateAsync(order, ct);
                throw new PaymentDomainException(
                    $"The PayPal authorization for order {orderId} has expired and can no longer be renewed. " +
                    "The order was returned to AwaitingPayment — ask the shopper to pay again, then fulfil.");
            }
        }

        var capture = await _paymentGateway.CaptureAsync(payment.AuthorizationId,
            CaptureKey(order.Id, payment.AuthorizationAttempt),
            payment.InvoiceId ?? InvoiceId(order.Id, payment.AuthorizationAttempt), ct);
        payment.SetCapture(capture.CaptureId, capture.Status, capture.Amount, capture.Fee, capture.Net,
            CaptureKey(order.Id, payment.AuthorizationAttempt));
        order.MarkFulfilled();
        await _orderRepository.UpdateAsync(order, ct);
        return order;
    }

    public async Task<Order> CancelOrderAsync(int orderId, CancellationToken ct)
    {
        var order = await GetOrderAsync(orderId, ct);

        if (order.Status == OrderStatus.Cancelled)
        {
            return order;
        }
        if (order.Status == OrderStatus.Fulfilled)
        {
            throw new PaymentDomainException($"Order {orderId} is already fulfilled; issue a refund instead of cancelling.");
        }

        var payment = order.Payment;
        if (payment?.AuthorizationId != null && payment.CaptureId == null)
        {
            var state = await _paymentGateway.GetAuthorizationAsync(payment.AuthorizationId, ct);
            payment.SetAuthorizationStatus(state.Status, state.ExpiresAt);
            if (IsCapturable(state))
            {
                await _paymentGateway.VoidAuthorizationAsync(payment.AuthorizationId,
                    VoidKey(order.Id, payment.AuthorizationAttempt), ct);
                payment.SetAuthorizationStatus("VOIDED", state.ExpiresAt);
            }
        }

        order.MarkCancelled();
        await _orderRepository.UpdateAsync(order, ct);
        return order;
    }

    public async Task<PaymentRefund> RefundOrderAsync(int orderId, decimal? amount, string idempotencyKey, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(idempotencyKey))
        {
            throw new PaymentDomainException("An idempotencyKey is required for refunds.", 400);
        }

        var order = await GetOrderAsync(orderId, ct);
        var payment = order.Payment;
        if (order.Status != OrderStatus.Fulfilled || payment?.CaptureId == null)
        {
            throw new PaymentDomainException($"Order {orderId} has no captured payment to refund.");
        }

        var existing = payment.Refunds.FirstOrDefault(r => r.IdempotencyKey == idempotencyKey);
        if (existing != null)
        {
            return existing;
        }

        var refundable = payment.RefundableAmount;
        var refundAmount = amount ?? refundable;
        if (refundAmount <= 0)
        {
            throw new PaymentDomainException($"Order {orderId} has already been refunded in full.");
        }
        if (refundAmount > refundable)
        {
            throw new PaymentDomainException(
                $"Refund of {refundAmount:F2} exceeds the refundable remainder of {refundable:F2} {payment.Currency}.", 422);
        }

        var result = await _paymentGateway.RefundCaptureAsync(payment.CaptureId, refundAmount, idempotencyKey, ct);
        var refund = payment.AddRefund(idempotencyKey, refundAmount, result.RefundId, result.Status);
        payment.SetCaptureStatus(payment.TotalRefunded >= payment.CapturedAmount ? "REFUNDED" : "PARTIALLY_REFUNDED");
        await _orderRepository.UpdateAsync(order, ct);
        return refund;
    }

    private async Task<Order> GetOrderAsync(int orderId, CancellationToken ct)
    {
        var order = await _orderRepository.FirstOrDefaultAsync(new OrderWithPaymentSpecification(orderId), ct);
        return order ?? throw new PaymentDomainException($"Order {orderId} was not found.", 404);
    }

    private static bool IsCapturable(AuthorizationState state) =>
        (state.Status == "CREATED" || state.Status == "PENDING") &&
        (state.ExpiresAt == null || state.ExpiresAt > DateTimeOffset.UtcNow);

    private static string AuthorizeKey(int orderId, int attempt) => $"eshop-{RunId}-order-{orderId}-authorize-{attempt}";
    private static string ReauthorizeKey(int orderId, int attempt) => $"eshop-{RunId}-order-{orderId}-reauthorize-{attempt}";
    private static string CaptureKey(int orderId, int attempt) => $"eshop-{RunId}-order-{orderId}-capture-{attempt}";
    private static string VoidKey(int orderId, int attempt) => $"eshop-{RunId}-order-{orderId}-void-{attempt}";

    // Unique per process run and per attempt: with the in-memory store, order ids
    // restart at 1 on every launch, and PayPal rejects an invoice id it has seen
    // before — even on a transaction it refused.
    private static readonly string RunId = Guid.NewGuid().ToString("N")[..8];

    public static string InvoiceId(int orderId, int attempt) => $"eshop-{RunId}-order-{orderId}-a{attempt}";
}
