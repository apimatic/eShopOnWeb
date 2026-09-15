using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Ardalis.GuardClauses;
using Microsoft.eShopWeb.ApplicationCore.Entities;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentMethodAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Interfaces.Payments;
using Microsoft.eShopWeb.ApplicationCore.Specifications;

namespace Microsoft.eShopWeb.ApplicationCore.Services;

public class OrderPaymentService : IOrderPaymentService
{
    private readonly IRepository<Order> _orderRepository;
    private readonly IRepository<CatalogItem> _itemRepository;
    private readonly IRepository<SavedPaymentMethod> _paymentMethodRepository;
    private readonly IPayPalPaymentGateway _payPal;
    private readonly IUriComposer _uriComposer;
    private readonly IAppLogger<OrderPaymentService> _logger;

    public OrderPaymentService(
        IRepository<Order> orderRepository,
        IRepository<CatalogItem> itemRepository,
        IRepository<SavedPaymentMethod> paymentMethodRepository,
        IPayPalPaymentGateway payPal,
        IUriComposer uriComposer,
        IAppLogger<OrderPaymentService> logger)
    {
        _orderRepository = orderRepository;
        _itemRepository = itemRepository;
        _paymentMethodRepository = paymentMethodRepository;
        _payPal = payPal;
        _uriComposer = uriComposer;
        _logger = logger;
    }

    public async Task<Order> PlaceOrderAsync(string buyerId, IReadOnlyCollection<OrderLineInput> lines, ShippingAddressInput? shippingAddress, CancellationToken cancellationToken = default)
    {
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));
        if (lines is null || lines.Count == 0)
        {
            throw new PaymentException(PaymentErrorReason.Validation, "An order must contain at least one line item.");
        }

        var normalized = lines
            .GroupBy(l => l.CatalogItemId)
            .Select(g => new OrderLineInput(g.Key, g.Sum(l => l.Quantity)))
            .ToList();

        if (normalized.Any(l => l.Quantity <= 0))
        {
            throw new PaymentException(PaymentErrorReason.Validation, "Every line item must have a quantity of at least 1.");
        }

        var ids = normalized.Select(l => l.CatalogItemId).ToArray();
        var catalogItems = await _itemRepository.ListAsync(new CatalogItemsSpecification(ids), cancellationToken);

        var missing = ids.Where(id => catalogItems.All(c => c.Id != id)).ToList();
        if (missing.Count > 0)
        {
            throw new PaymentException(PaymentErrorReason.Validation, $"Unknown catalog item id(s): {string.Join(", ", missing)}.");
        }

        var items = normalized.Select(line =>
        {
            var catalogItem = catalogItems.First(c => c.Id == line.CatalogItemId);
            var itemOrdered = new CatalogItemOrdered(catalogItem.Id, catalogItem.Name, _uriComposer.ComposePicUri(catalogItem.PictureUri));
            return new OrderItem(itemOrdered, catalogItem.Price, line.Quantity);
        }).ToList();

        var address = shippingAddress is null
            ? new Address("N/A", "N/A", "N/A", "N/A", "N/A")
            : new Address(shippingAddress.Street, shippingAddress.City, shippingAddress.State, shippingAddress.Country, shippingAddress.ZipCode);

        var order = new Order(buyerId, address, items);
        order = await _orderRepository.AddAsync(order, cancellationToken);
        _logger.LogInformation("Placed order {0} for buyer {1} ({2} line(s), total {3}).", order.Id, buyerId, items.Count, order.Total());
        return order;
    }

    public async Task<Order> AuthorizeAsync(string buyerId, int orderId, PayInstruction instruction, CancellationToken cancellationToken = default)
    {
        var order = await LoadOwnedOrderAsync(buyerId, orderId, cancellationToken);

        // Idempotent in effect: a double-click never authorizes twice.
        if (order.Payment?.HasAuthorization == true)
        {
            _logger.LogInformation("Order {0} is already authorized ({1}); returning existing hold.", orderId, order.Payment.AuthorizationId);
            return order;
        }

        if (order.Status != OrderStatus.AwaitingPayment)
        {
            throw new PaymentException(PaymentErrorReason.Conflict, $"Order {orderId} cannot be paid because it is {order.Status}.");
        }

        var currency = _payPal.Currency;
        var amount = order.Total();
        if (amount <= 0m)
        {
            throw new PaymentException(PaymentErrorReason.Validation, $"Order {orderId} has a non-positive total and cannot be paid.");
        }

        var (source, savedDescription) = await ResolvePaymentSourceAsync(buyerId, instruction, cancellationToken);

        var payment = order.EnsurePayment(currency, amount);

        // 1) Create the PayPal order (intent=AUTHORIZE) if not already created. invoice_id/custom_id carry the
        //    eShop order id so PayPal's records can be reconciled back to eShop.
        if (string.IsNullOrEmpty(payment.PayPalOrderId))
        {
            // PayPal enforces unique invoice ids per account, so scope it with the payment nonce.
            var invoiceReference = $"ESHOP-{orderId}-{payment.Nonce[..8]}";
            var payPalOrderId = await _payPal.CreateOrderForAuthorizationAsync(
                amount, currency, invoiceReference, $"eshop-order-{payment.Nonce}", cancellationToken);
            payment.RecordPayPalOrder(payPalOrderId, invoiceReference);
            await _orderRepository.UpdateAsync(order, cancellationToken);
        }

        // 2) Authorize (place the hold) with the chosen card.
        var auth = await _payPal.AuthorizeOrderAsync(payment.PayPalOrderId!, source, $"eshop-auth-{payment.Nonce}", cancellationToken);

        payment.RecordAuthorization(auth.AuthorizationId, auth.Status, auth.ExpiresAt, savedDescription ?? auth.InstrumentDescription);
        order.MarkAuthorized();
        await _orderRepository.UpdateAsync(order, cancellationToken);

        _logger.LogInformation("Authorized order {0}: hold {1} for {2} {3} (status {4}).", orderId, auth.AuthorizationId, amount, currency, auth.Status);
        return order;
    }

    public async Task<Order> FulfilAsync(int orderId, CancellationToken cancellationToken = default)
    {
        var order = await LoadOrderAsync(orderId, cancellationToken);
        var payment = order.Payment;

        if (order.Status == OrderStatus.Fulfilled || payment?.HasCapture == true)
        {
            _logger.LogInformation("Order {0} is already fulfilled ({1}); nothing to capture.", orderId, payment?.CaptureId);
            return order;
        }

        if (order.Status != OrderStatus.PaymentAuthorized || payment is null || !payment.HasAuthorization)
        {
            throw new PaymentException(PaymentErrorReason.Conflict, $"Order {orderId} cannot be fulfilled because it is {order.Status} (no active payment hold).");
        }

        var amount = payment.Amount;
        var currency = payment.Currency;

        // If the hold has gone stale before fulfilment, renew it rather than failing the fulfilment outright.
        if (IsAuthorizationStale(payment))
        {
            _logger.LogWarning("Order {0} hold {1} is stale (expires {2}); renewing before capture.", orderId, payment.AuthorizationId, payment.AuthorizationExpiresAt);
            await RenewAuthorizationOrThrowAsync(order, payment, amount, currency, cancellationToken);
        }

        PayPalCaptureResult capture;
        try
        {
            // The capture key is derived from the authorization id (globally unique, and it changes on renewal),
            // so a retried fulfilment de-duplicates while distinct authorizations never collide.
            capture = await _payPal.CaptureAuthorizationAsync(payment.AuthorizationId!, amount, currency, payment.InvoiceReference ?? InvoiceId(orderId), $"eshop-capture-{payment.AuthorizationId}", cancellationToken);
        }
        catch (PayPalApiException ex) when (IsRenewableCaptureFailure(ex))
        {
            // Reactive fallback: the hold was stale in a way we didn't predict — renew and capture once more.
            _logger.LogWarning("Capture of order {0} failed ({1}); attempting to renew the hold and re-capture.", orderId, string.Join(",", ex.Issues));
            await RenewAuthorizationOrThrowAsync(order, payment, amount, currency, cancellationToken);
            capture = await _payPal.CaptureAuthorizationAsync(payment.AuthorizationId!, amount, currency, payment.InvoiceReference ?? InvoiceId(orderId), $"eshop-capture-{payment.AuthorizationId}", cancellationToken);
        }

        payment.RecordCapture(capture.CaptureId, capture.Status, capture.Gross, capture.Fee, capture.Net);
        order.MarkFulfilled();
        await _orderRepository.UpdateAsync(order, cancellationToken);

        _logger.LogInformation("Fulfilled order {0}: captured {1} {2} (fee {3}, net {4}) via capture {5}.",
            orderId, capture.Gross, capture.CurrencyCode, capture.Fee, capture.Net, capture.CaptureId);
        return order;
    }

    public async Task<Order> CancelAsync(int orderId, CancellationToken cancellationToken = default)
    {
        var order = await LoadOrderAsync(orderId, cancellationToken);
        var payment = order.Payment;

        if (order.Status == OrderStatus.Cancelled)
        {
            return order;
        }

        if (order.Status == OrderStatus.Fulfilled || payment?.HasCapture == true)
        {
            throw new PaymentException(PaymentErrorReason.Conflict, $"Order {orderId} has been fulfilled and can no longer be cancelled; issue a refund instead.");
        }

        // Release the hold at PayPal if one exists, so no money ever moved.
        if (payment?.HasAuthorization == true && !string.Equals(payment.AuthorizationStatus, "VOIDED", StringComparison.OrdinalIgnoreCase))
        {
            await _payPal.VoidAuthorizationAsync(payment.AuthorizationId!, cancellationToken);
            payment.MarkAuthorizationVoided();
        }

        order.MarkCancelled();
        await _orderRepository.UpdateAsync(order, cancellationToken);
        _logger.LogInformation("Cancelled order {0}; any held funds were released.", orderId);
        return order;
    }

    public async Task<PaymentRefund> RefundAsync(string buyerId, int orderId, decimal? amount, string idempotencyKey, CancellationToken cancellationToken = default)
    {
        Guard.Against.NullOrEmpty(idempotencyKey, nameof(idempotencyKey));
        var order = await LoadOwnedOrderAsync(buyerId, orderId, cancellationToken);
        var payment = order.Payment;

        if (payment is null || !payment.HasCapture)
        {
            throw new PaymentException(PaymentErrorReason.Conflict, $"Order {orderId} has no captured payment to refund.");
        }

        // Idempotent under the caller-supplied key: repeating the same request never refunds twice.
        var existing = payment.FindRefundByIdempotencyKey(idempotencyKey);
        if (existing is not null)
        {
            _logger.LogInformation("Refund for order {0} under key {1} already exists ({2}); returning it.", orderId, idempotencyKey, existing.RefundId);
            return existing;
        }

        var refundAmount = amount ?? payment.RefundableRemaining;
        if (refundAmount <= 0m)
        {
            throw new PaymentException(PaymentErrorReason.Validation, "Refund amount must be a positive number.");
        }

        // Guard before calling PayPal: a partly-refunded order must never become refundable beyond what was captured.
        if (refundAmount - payment.RefundableRemaining > 0.0001m)
        {
            throw new PaymentException(PaymentErrorReason.Validation,
                $"Refund of {refundAmount:0.00} exceeds the refundable remaining of {payment.RefundableRemaining:0.00} {payment.Currency} for order {orderId}.");
        }

        // The PayPal-Request-Id is namespaced by the capture id so the same (capture, key) de-duplicates at PayPal
        // and never collides across runs, while distinct keys remain distinct legitimate partial refunds.
        var payPalRequestId = $"eshop-refund-{payment.CaptureId}-{idempotencyKey}";
        var result = await _payPal.RefundCaptureAsync(payment.CaptureId!, refundAmount, payment.Currency, payPalRequestId, cancellationToken);

        var refund = payment.AddRefund(result.RefundId, result.Amount, result.Status, idempotencyKey);
        order.RefreshRefundStatus();
        await _orderRepository.UpdateAsync(order, cancellationToken);

        _logger.LogInformation("Refunded {0} {1} on order {2} (refund {3}, status {4}); remaining refundable {5}.",
            result.Amount, result.CurrencyCode, orderId, result.RefundId, result.Status, payment.RefundableRemaining);
        return refund;
    }

    public async Task<IReadOnlyList<Order>> GetOrdersForBuyerAsync(string buyerId, CancellationToken cancellationToken = default)
    {
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));
        var orders = await _orderRepository.ListAsync(new CustomerOrdersWithPaymentSpecification(buyerId), cancellationToken);
        return orders.OrderByDescending(o => o.OrderDate).ToList();
    }

    public async Task<Order?> GetOrderForBuyerAsync(string buyerId, int orderId, CancellationToken cancellationToken = default)
    {
        var order = await _orderRepository.FirstOrDefaultAsync(new OrderWithPaymentByIdSpecification(orderId), cancellationToken);
        if (order is null || order.BuyerId != buyerId)
        {
            return null;
        }
        return order;
    }

    // --- helpers ---------------------------------------------------------------------------------

    private static string InvoiceId(int orderId) => $"ESHOP-{orderId}";

    private async Task<Order> LoadOrderAsync(int orderId, CancellationToken cancellationToken)
    {
        var order = await _orderRepository.FirstOrDefaultAsync(new OrderWithPaymentByIdSpecification(orderId), cancellationToken);
        if (order is null)
        {
            throw new PaymentException(PaymentErrorReason.NotFound, $"Order {orderId} was not found.");
        }
        return order;
    }

    private async Task<Order> LoadOwnedOrderAsync(string buyerId, int orderId, CancellationToken cancellationToken)
    {
        var order = await LoadOrderAsync(orderId, cancellationToken);
        if (order.BuyerId != buyerId)
        {
            // Do not reveal the existence of another shopper's order.
            throw new PaymentException(PaymentErrorReason.NotFound, $"Order {orderId} was not found.");
        }
        return order;
    }

    private async Task<(PayPalPaymentSource Source, string? SavedDescription)> ResolvePaymentSourceAsync(string buyerId, PayInstruction instruction, CancellationToken cancellationToken)
    {
        switch (instruction)
        {
            case PayWithSavedCardInstruction saved:
                var method = await _paymentMethodRepository.FirstOrDefaultAsync(new SavedPaymentMethodByIdSpecification(saved.PaymentMethodId, buyerId), cancellationToken);
                if (method is null)
                {
                    throw new PaymentException(PaymentErrorReason.Validation, $"Saved card {saved.PaymentMethodId} was not found for this shopper.");
                }
                return (new VaultedCardPaymentSource(method.PayPalVaultId), method.DisplayName);

            case PayWithCardInstruction card:
                return (new CardPaymentSource(card.Card), null);

            default:
                throw new PaymentException(PaymentErrorReason.Validation, "A card or a saved card must be supplied to pay.");
        }
    }

    private static bool IsAuthorizationStale(OrderPayment payment)
    {
        // The stored expiry is PayPal's honor-period expiry for the hold. Once past, capture may fail,
        // so we proactively renew. A small skew guards against clock differences.
        return payment.AuthorizationExpiresAt is { } expiry && DateTimeOffset.UtcNow >= expiry.AddMinutes(-1);
    }

    private static bool IsRenewableCaptureFailure(PayPalApiException ex)
    {
        // A capture that fails because the hold has expired is renewable via reauthorize. Match the precise
        // "expired" signal so unrelated authorization errors (e.g. already captured) are not retried as renewals.
        return ex.StatusCode is 422 or 400
            && ex.Issues.Any(i => i.Contains("EXPIRED", StringComparison.OrdinalIgnoreCase));
    }

    private async Task RenewAuthorizationOrThrowAsync(Order order, OrderPayment payment, decimal amount, string currency, CancellationToken cancellationToken)
    {
        try
        {
            var renewed = await _payPal.ReauthorizeAsync(payment.AuthorizationId!, amount, currency, $"eshop-reauth-{payment.AuthorizationId}", cancellationToken);
            payment.RenewAuthorization(renewed.AuthorizationId, renewed.Status, renewed.ExpiresAt);
            await _orderRepository.UpdateAsync(order, cancellationToken);
            _logger.LogInformation("Renewed hold on order {0}: new authorization {1} (status {2}).", order.Id, renewed.AuthorizationId, renewed.Status);
        }
        catch (PayPalApiException ex)
        {
            var detail = ex.Issues.Count > 0 ? string.Join(", ", ex.Issues) : ex.Message;
            throw new PaymentException(
                PaymentErrorReason.AuthorizationUnrenewable,
                $"The payment hold for order {order.Id} has expired and can no longer be renewed (PayPal: {detail}; debug id {ex.DebugId ?? "n/a"}). " +
                "Ask the shopper to place and pay for the order again.",
                ex);
        }
    }
}
