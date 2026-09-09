using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentMethodAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Specifications;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.Infrastructure.Services.PayPal;

/// <summary>
/// Orchestrates the order-payment lifecycle against PayPal while keeping eShop's own record of the
/// money movement. Operations are idempotent in effect and enforce shopper ownership.
/// </summary>
public class PaymentProcessingService : IPaymentProcessingService
{
    // A short token unique to this process, mixed into invoice ids so that in-memory order ids that
    // restart at 1 on each run do not collide with a previous run's PayPal invoice ids.
    private static readonly string RunToken = Guid.NewGuid().ToString("N")[..8];

    private readonly IRepository<Order> _orderRepository;
    private readonly IReadRepository<CatalogItem> _itemRepository;
    private readonly IReadRepository<SavedPaymentMethod> _savedCardRepository;
    private readonly IPayPalClient _payPal;
    private readonly IUriComposer _uriComposer;
    private readonly IAppLogger<PaymentProcessingService> _logger;
    private readonly string _currency;

    public PaymentProcessingService(
        IRepository<Order> orderRepository,
        IReadRepository<CatalogItem> itemRepository,
        IReadRepository<SavedPaymentMethod> savedCardRepository,
        IPayPalClient payPal,
        IUriComposer uriComposer,
        IOptions<PayPalSettings> settings,
        IAppLogger<PaymentProcessingService> logger)
    {
        _orderRepository = orderRepository;
        _itemRepository = itemRepository;
        _savedCardRepository = savedCardRepository;
        _payPal = payPal;
        _uriComposer = uriComposer;
        _logger = logger;
        _currency = settings.Value.Currency;
    }

    public async Task<int> PlaceOrderAsync(string buyerId, IReadOnlyCollection<OrderLine> lines, ShippingAddress? shipTo, CancellationToken cancellationToken = default)
    {
        if (lines is null || lines.Count == 0)
        {
            throw new PaymentValidationException("An order must contain at least one item.");
        }
        if (lines.Any(l => l.Quantity <= 0))
        {
            throw new PaymentValidationException("Every order line must have a quantity greater than zero.");
        }

        var ids = lines.Select(l => l.CatalogItemId).Distinct().ToArray();
        var catalogItems = await _itemRepository.ListAsync(new CatalogItemsSpecification(ids), cancellationToken);

        var missing = ids.Where(id => catalogItems.All(c => c.Id != id)).ToArray();
        if (missing.Length > 0)
        {
            throw new PaymentValidationException($"Unknown catalog item id(s): {string.Join(", ", missing)}.");
        }

        var items = lines.Select(line =>
        {
            var catalogItem = catalogItems.First(c => c.Id == line.CatalogItemId);
            var itemOrdered = new CatalogItemOrdered(catalogItem.Id, catalogItem.Name, _uriComposer.ComposePicUri(catalogItem.PictureUri));
            return new OrderItem(itemOrdered, catalogItem.Price, line.Quantity);
        }).ToList();

        var address = shipTo is null
            ? new Address("N/A", "N/A", "N/A", "N/A", "00000")
            : new Address(shipTo.Street, shipTo.City, shipTo.State, shipTo.Country, shipTo.ZipCode);

        var order = new Order(buyerId, address, items);
        order = await _orderRepository.AddAsync(order, cancellationToken);
        _logger.LogInformation($"Placed order {order.Id} for buyer (total {order.Total():0.00} {_currency}).");
        return order.Id;
    }

    public async Task<Order> PayAsync(int orderId, string buyerId, PayInstruction instruction, CancellationToken cancellationToken = default)
    {
        var order = await LoadOwnedOrderAsync(orderId, buyerId, cancellationToken);

        // Idempotency: an order that already has a payment is not authorized again.
        if (order.Payment is not null)
        {
            _logger.LogInformation($"Order {orderId} is already paid; returning existing payment (idempotent).");
            return order;
        }

        var total = order.Total();
        if (total <= 0m)
        {
            throw new PaymentValidationException("The order total must be greater than zero to take a payment.");
        }

        var money = new PayPalMoney(total, _currency);
        var invoiceId = InvoiceIdFor(orderId);
        var customId = $"order-{orderId}";
        var requestId = RequestId("authorize", orderId);

        PayPalAuthorizationResult auth;
        PaymentSourceType sourceType;
        string? brand;
        string? last4;

        if (instruction.Card is not null)
        {
            auth = await _payPal.AuthorizeWithCardAsync(money, invoiceId, customId, instruction.Card, requestId, cancellationToken);
            sourceType = PaymentSourceType.Card;
            brand = auth.CardBrand;
            last4 = auth.CardLast4;
        }
        else if (instruction.SavedPaymentMethodId is int savedId)
        {
            var savedCard = await _savedCardRepository.FirstOrDefaultAsync(
                new SavedPaymentMethodByIdForBuyerSpecification(savedId, buyerId), cancellationToken);
            if (savedCard is null)
            {
                throw new PaymentValidationException($"Saved card {savedId} was not found for this shopper.");
            }
            auth = await _payPal.AuthorizeWithVaultedCardAsync(money, invoiceId, customId, savedCard.PayPalVaultTokenId, requestId, cancellationToken);
            sourceType = PaymentSourceType.SavedCard;
            brand = auth.CardBrand ?? savedCard.CardBrand;
            last4 = auth.CardLast4 ?? savedCard.CardLast4;
        }
        else
        {
            throw new PaymentValidationException("Provide either card details or a saved card id to pay.");
        }

        var payment = new Payment(
            auth.PayPalOrderId, invoiceId, total, _currency, sourceType,
            auth.AuthorizationId, auth.Status, auth.ExpiresAt, brand, last4);

        order.SetPayment(payment);
        await _orderRepository.UpdateAsync(order, cancellationToken);
        _logger.LogInformation($"Authorized order {orderId}: hold {auth.AuthorizationId} for {total:0.00} {_currency}.");
        return order;
    }

    public async Task<Order> FulfilAsync(int orderId, CancellationToken cancellationToken = default)
    {
        var order = await LoadOrderAsync(orderId, cancellationToken);
        var payment = RequirePayment(order);

        // Idempotency: a payment already captured (or refunded) is treated as already fulfilled.
        if (payment.Status is PaymentStatus.Captured or PaymentStatus.PartiallyRefunded or PaymentStatus.Refunded)
        {
            _logger.LogInformation($"Order {orderId} is already fulfilled (payment {payment.Status}).");
            return order;
        }
        if (payment.Status == PaymentStatus.Voided)
        {
            throw new PaymentStateException("This order was cancelled; it cannot be fulfilled.");
        }

        // Renew a hold that has already gone stale before attempting to capture.
        if (payment.AuthorizationExpiresAt is { } expiry && expiry <= DateTimeOffset.UtcNow)
        {
            await RenewAuthorizationAsync(order, payment, cancellationToken);
        }

        PayPalCaptureResult capture;
        var captureRequestId = RequestId("capture", orderId);
        try
        {
            capture = await _payPal.CaptureAsync(payment.AuthorizationId, captureRequestId, cancellationToken);
        }
        catch (PayPalGatewayException ex) when (IsExpiredAuthorization(ex))
        {
            // The hold lapsed between the check and the capture: renew, then capture once more.
            await RenewAuthorizationAsync(order, payment, cancellationToken);
            capture = await _payPal.CaptureAsync(payment.AuthorizationId, captureRequestId, cancellationToken);
        }

        payment.MarkCaptured(capture.CaptureId, capture.Status, capture.GrossAmount, capture.PayPalFee, capture.NetAmount);
        await _orderRepository.UpdateAsync(order, cancellationToken);
        _logger.LogInformation($"Fulfilled order {orderId}: captured {capture.GrossAmount:0.00} (fee {capture.PayPalFee:0.00}, net {capture.NetAmount:0.00}).");
        return order;
    }

    public async Task<Order> CancelAsync(int orderId, CancellationToken cancellationToken = default)
    {
        var order = await LoadOrderAsync(orderId, cancellationToken);
        var payment = RequirePayment(order);

        if (payment.Status == PaymentStatus.Voided)
        {
            _logger.LogInformation($"Order {orderId} is already cancelled.");
            return order;
        }
        if (payment.Status is PaymentStatus.Captured or PaymentStatus.PartiallyRefunded or PaymentStatus.Refunded)
        {
            throw new PaymentStateException("This order has been fulfilled; issue a refund instead of cancelling.");
        }

        await _payPal.VoidAsync(payment.AuthorizationId, RequestId("void", orderId), cancellationToken);
        payment.MarkVoided();
        await _orderRepository.UpdateAsync(order, cancellationToken);
        _logger.LogInformation($"Cancelled order {orderId}: released hold {payment.AuthorizationId}.");
        return order;
    }

    public async Task<RefundOutcome> RefundAsync(int orderId, string buyerId, decimal? amount, string idempotencyKey, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(idempotencyKey))
        {
            throw new PaymentValidationException("A refund idempotency key is required.");
        }

        var order = await LoadOwnedOrderAsync(orderId, buyerId, cancellationToken);
        var payment = RequirePayment(order);

        if (payment.CaptureId is null)
        {
            throw new PaymentStateException("This order has not been fulfilled; there is nothing to refund.");
        }

        // Idempotency: repeating a refund under the same key returns the original refund.
        var existing = payment.FindRefundByKey(idempotencyKey);
        if (existing is not null)
        {
            _logger.LogInformation($"Refund key '{idempotencyKey}' already applied to order {orderId}; returning original refund.");
            return new RefundOutcome(existing.RefundId, order);
        }

        var refundAmount = amount ?? payment.RefundableRemaining();
        payment.EnsureRefundable(refundAmount);

        var money = amount.HasValue ? new PayPalMoney(amount.Value, _currency) : null;
        var result = await _payPal.RefundAsync(payment.CaptureId, money, payment.InvoiceId, idempotencyKey, cancellationToken);

        var recordedAmount = result.Amount > 0m ? result.Amount : refundAmount;
        payment.AddRefund(result.RefundId, recordedAmount, result.Status, idempotencyKey);
        await _orderRepository.UpdateAsync(order, cancellationToken);
        _logger.LogInformation($"Refunded {recordedAmount:0.00} on order {orderId} (refund {result.RefundId}).");
        return new RefundOutcome(result.RefundId, order);
    }

    public async Task<IReadOnlyList<Order>> GetOrdersForBuyerAsync(string buyerId, CancellationToken cancellationToken = default)
    {
        var orders = await _orderRepository.ListAsync(new CustomerOrdersWithPaymentSpecification(buyerId), cancellationToken);
        return orders;
    }

    public async Task<Order?> GetOrderForBuyerAsync(int orderId, string buyerId, CancellationToken cancellationToken = default)
    {
        var order = await _orderRepository.FirstOrDefaultAsync(new OrderWithPaymentByIdSpecification(orderId), cancellationToken);
        if (order is null || !order.BuyerId.Equals(buyerId, StringComparison.Ordinal))
        {
            return null;
        }
        return order;
    }

    private async Task RenewAuthorizationAsync(Order order, Payment payment, CancellationToken cancellationToken)
    {
        _logger.LogInformation($"Renewing stale authorization {payment.AuthorizationId} for order {order.Id}.");
        try
        {
            var money = new PayPalMoney(payment.Amount, payment.CurrencyCode);
            var reauth = await _payPal.ReauthorizeAsync(payment.AuthorizationId, money, RequestId("reauthorize", order.Id), cancellationToken);
            payment.RenewAuthorization(reauth.AuthorizationId, reauth.Status, reauth.ExpiresAt);
            await _orderRepository.UpdateAsync(order, cancellationToken);
        }
        catch (PayPalGatewayException ex)
        {
            throw new PaymentStateException(
                $"The authorization for order {order.Id} has expired and can no longer be renewed " +
                $"({ex.PayPalErrorName ?? "not renewable"}). Collect a new payment from the shopper before fulfilling.", ex);
        }
    }

    private static bool IsExpiredAuthorization(PayPalGatewayException ex)
    {
        var name = ex.PayPalErrorName ?? string.Empty;
        return name.Contains("EXPIRED", StringComparison.OrdinalIgnoreCase)
            || ex.Message.Contains("EXPIRED", StringComparison.OrdinalIgnoreCase);
    }

    private async Task<Order> LoadOrderAsync(int orderId, CancellationToken cancellationToken)
    {
        var order = await _orderRepository.FirstOrDefaultAsync(new OrderWithPaymentByIdSpecification(orderId), cancellationToken);
        if (order is null)
        {
            throw new OrderNotFoundException(orderId);
        }
        return order;
    }

    private async Task<Order> LoadOwnedOrderAsync(int orderId, string buyerId, CancellationToken cancellationToken)
    {
        var order = await LoadOrderAsync(orderId, cancellationToken);
        if (!order.BuyerId.Equals(buyerId, StringComparison.Ordinal))
        {
            // Do not reveal that another shopper's order exists.
            throw new OrderNotFoundException(orderId);
        }
        return order;
    }

    private static Payment RequirePayment(Order order)
    {
        if (order.Payment is null)
        {
            throw new PaymentStateException($"Order {order.Id} has not been paid yet.");
        }
        return order.Payment;
    }

    private static string InvoiceIdFor(int orderId) =>
        string.Create(CultureInfo.InvariantCulture, $"eshop-{orderId}-{RunToken}");

    // Idempotency keys are scoped to this process run so a double-click within a run is idempotent,
    // while a fresh run (in-memory order ids restart at 1) never replays a previous run's stale result.
    private static string RequestId(string action, int orderId) => $"{action}-{RunToken}-order-{orderId}";
}
