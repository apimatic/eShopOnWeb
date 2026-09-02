using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Specifications;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.PublicApi.Payments;

/// <summary>
/// Orchestrates order payment state (authorize at checkout, capture at fulfilment, void on
/// cancel, refund on return) against the payment gateway. All operations are idempotent in
/// effect: repeating one returns the already-recorded outcome instead of charging twice.
/// </summary>
public class OrderPaymentService
{
    // PayPal-Request-Id values must be unique for the merchant account, which outlives any
    // single run of this app — so caller-supplied refund keys are namespaced per run. The
    // primary idempotency mechanism is the locally persisted (key -> refund) record.
    private static readonly string RunId = Guid.NewGuid().ToString("N");

    private readonly IRepository<Order> _orderRepository;
    private readonly IRepository<Payment> _paymentRepository;
    private readonly IRepository<CatalogItem> _catalogItemRepository;
    private readonly IRepository<SavedPaymentMethod> _savedCardRepository;
    private readonly IRepository<PaymentRefund> _refundRepository;
    private readonly IPaymentGateway _gateway;
    private readonly PayPalSettings _settings;
    private readonly IAppLogger<OrderPaymentService> _logger;

    public OrderPaymentService(
        IRepository<Order> orderRepository,
        IRepository<Payment> paymentRepository,
        IRepository<CatalogItem> catalogItemRepository,
        IRepository<SavedPaymentMethod> savedCardRepository,
        IRepository<PaymentRefund> refundRepository,
        IPaymentGateway gateway,
        IOptions<PayPalSettings> settings,
        IAppLogger<OrderPaymentService> logger)
    {
        _orderRepository = orderRepository;
        _paymentRepository = paymentRepository;
        _catalogItemRepository = catalogItemRepository;
        _savedCardRepository = savedCardRepository;
        _refundRepository = refundRepository;
        _gateway = gateway;
        _settings = settings.Value;
        _logger = logger;
    }

    public string Currency => _settings.Currency;

    public async Task<Order> CreateOrderAsync(string buyerId, IReadOnlyList<(int CatalogItemId, int Quantity)> items,
        Address shipToAddress, CancellationToken ct)
    {
        if (items.Count == 0)
        {
            throw new InvalidOperationException("An order must contain at least one item.");
        }
        if (items.Any(i => i.Quantity <= 0))
        {
            throw new InvalidOperationException("Item quantities must be positive.");
        }

        var catalogItems = await _catalogItemRepository.ListAsync(
            new CatalogItemsSpecification(items.Select(i => i.CatalogItemId).Distinct().ToArray()), ct);
        var missing = items.Select(i => i.CatalogItemId).Distinct().Except(catalogItems.Select(c => c.Id)).ToList();
        if (missing.Count > 0)
        {
            throw new InvalidOperationException($"Unknown catalog item id(s): {string.Join(", ", missing)}.");
        }

        var orderItems = items.Select(i =>
        {
            var catalogItem = catalogItems.First(c => c.Id == i.CatalogItemId);
            return new OrderItem(
                new CatalogItemOrdered(catalogItem.Id, catalogItem.Name, catalogItem.PictureUri),
                catalogItem.Price,
                i.Quantity);
        }).ToList();

        var order = new Order(buyerId, shipToAddress, orderItems);
        return await _orderRepository.AddAsync(order, ct);
    }

    public async Task<(Order Order, Payment Payment, bool AlreadyAuthorized)> PayAsync(string buyerId, int orderId,
        GatewayCard? card, int? savedPaymentMethodId, CancellationToken ct)
    {
        var order = await GetOwnedOrderAsync(buyerId, orderId, ct);

        if (order.Status == OrderStatus.PaymentAuthorized && order.Payment is not null)
        {
            return (order, order.Payment, true);
        }
        if (order.Status != OrderStatus.AwaitingPayment)
        {
            throw new InvalidOperationException($"Order {orderId} is {order.Status} and cannot be paid.");
        }

        string? vaultId = null;
        string description;
        if (savedPaymentMethodId.HasValue)
        {
            var savedCard = await _savedCardRepository.GetByIdAsync(savedPaymentMethodId.Value, ct);
            if (savedCard is null || savedCard.BuyerId != buyerId)
            {
                throw new InvalidOperationException($"Saved payment method {savedPaymentMethodId} was not found.");
            }
            vaultId = savedCard.VaultTokenId;
            description = $"{savedCard.Brand} ****{savedCard.LastDigits} (saved)";
        }
        else if (card is not null)
        {
            description = $"Card ending {(card.Number.Length >= 4 ? card.Number[^4..] : "****")}";
        }
        else
        {
            throw new InvalidOperationException("Provide either card details or a saved paymentMethodId.");
        }

        // Persist the attempt (with its merchant-unique invoice id and idempotency key
        // material) BEFORE calling PayPal: a repeated call for the same attempt replays the
        // stored key, while a retry after a failure resets the row with a fresh invoice id.
        var payment = order.Payment;
        if (payment is null)
        {
            payment = new Payment(order.Id, NewInvoiceId(orderId), Currency, description);
            await _paymentRepository.AddAsync(payment, ct);
        }
        else
        {
            payment.ResetForRetry(NewInvoiceId(orderId), description);
        }
        var authorizeKey = payment.NextOperationKey("authorize");
        await _paymentRepository.UpdateAsync(payment, ct);

        GatewayAuthorizationResult authorization;
        try
        {
            authorization = await _gateway.AuthorizeAsync(
                payment.InvoiceId, order.Total(), Currency, card, vaultId, authorizeKey, ct);
        }
        catch
        {
            payment.MarkFailed();
            await _paymentRepository.UpdateAsync(payment, ct);
            throw;
        }

        payment.MarkAuthorized(authorization.PayPalOrderId, authorization.AuthorizationId,
            authorization.Status, authorization.ExpiresAt);
        order.MarkPaymentAuthorized();
        await _orderRepository.UpdateAsync(order, ct);

        _logger.LogInformation("Order {OrderId} authorized via PayPal authorization {AuthorizationId}.",
            order.Id, authorization.AuthorizationId);
        return (order, payment, false);
    }

    public async Task<(Order Order, Payment Payment, bool AlreadyFulfilled)> FulfilAsync(int orderId, CancellationToken ct)
    {
        var order = await GetOrderAsync(orderId, ct);

        if (order.Status == OrderStatus.Fulfilled && order.Payment?.CaptureId is not null)
        {
            return (order, order.Payment, true);
        }
        if (order.Status != OrderStatus.PaymentAuthorized || order.Payment is null)
        {
            throw new InvalidOperationException($"Order {orderId} is {order.Status} and cannot be fulfilled.");
        }

        var payment = order.Payment;
        var authorization = await _gateway.GetAuthorizationAsync(payment.AuthorizationId, ct);

        var stale = authorization.Status != "CREATED"
            || (authorization.ExpiresAt.HasValue && authorization.ExpiresAt.Value <= DateTimeOffset.UtcNow.AddMinutes(5));
        if (stale)
        {
            if (authorization.Status is "VOIDED" or "DENIED")
            {
                throw new InvalidOperationException(
                    $"The PayPal authorization for order {orderId} is {authorization.Status} and can no longer be renewed. " +
                    "Cancel the order and ask the customer to pay again.");
            }

            GatewayAuthorizationStatus renewed;
            try
            {
                var reauthorizeKey = payment.NextOperationKey("reauthorize");
                await _paymentRepository.UpdateAsync(payment, ct);
                renewed = await _gateway.ReauthorizeAsync(payment.AuthorizationId!, order.Total(), Currency,
                    reauthorizeKey, ct);
            }
            catch (PaymentGatewayException ex) when (ex.ProviderStatus is >= 400 and < 500)
            {
                throw new InvalidOperationException(
                    $"The PayPal authorization for order {orderId} has expired and can no longer be renewed " +
                    "(PayPal allows renewal only within 29 days of the original authorization). " +
                    "Cancel the order and ask the customer to pay again.", ex);
            }
            payment.UpdateAuthorization(renewed.AuthorizationId, renewed.Status, renewed.ExpiresAt);
        }

        var captureKey = payment.NextOperationKey("capture");
        await _paymentRepository.UpdateAsync(payment, ct);
        var capture = await _gateway.CaptureAsync(payment.AuthorizationId!, payment.InvoiceId,
            captureKey, ct);
        if (capture.Status == "DECLINED" || capture.Status == "FAILED")
        {
            throw new PaymentGatewayException(422, $"PayPal could not capture the payment for order {orderId}: capture {capture.Status}.");
        }

        payment.MarkCaptured(capture.CaptureId, capture.Status, capture.GrossAmount, capture.PayPalFee, capture.NetAmount);
        order.MarkFulfilled();
        await _orderRepository.UpdateAsync(order, ct);

        _logger.LogInformation("Order {OrderId} fulfilled; captured {Amount} via PayPal capture {CaptureId}.",
            order.Id, capture.GrossAmount, capture.CaptureId);
        return (order, payment, false);
    }

    public async Task<Order> CancelAsync(int orderId, CancellationToken ct)
    {
        var order = await GetOrderAsync(orderId, ct);

        if (order.Status == OrderStatus.Cancelled)
        {
            return order;
        }
        if (order.Status == OrderStatus.Fulfilled)
        {
            throw new InvalidOperationException(
                $"Order {orderId} is already fulfilled and cannot be cancelled; issue a refund instead.");
        }

        if (order.Status == OrderStatus.PaymentAuthorized && order.Payment is not null)
        {
            var voidKey = order.Payment.NextOperationKey("void");
            await _paymentRepository.UpdateAsync(order.Payment, ct);
            var status = await _gateway.VoidAsync(order.Payment.AuthorizationId!, voidKey, ct);
            order.Payment.MarkVoided(status);
        }

        order.MarkCancelled();
        await _orderRepository.UpdateAsync(order, ct);

        _logger.LogInformation("Order {OrderId} cancelled; any held funds released.", order.Id);
        return order;
    }

    public async Task<(Order Order, PaymentRefund Refund, bool AlreadyRefunded)> RefundAsync(int orderId,
        decimal? amount, string idempotencyKey, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(idempotencyKey))
        {
            throw new InvalidOperationException("An idempotencyKey is required for refunds.");
        }

        var existing = await _refundRepository.FirstOrDefaultAsync(
            new PaymentRefundByIdempotencyKeySpecification(idempotencyKey), ct);
        if (existing is not null)
        {
            var existingOrder = await GetOrderAsync(orderId, ct);
            return (existingOrder, existing, true);
        }

        var order = await GetOrderAsync(orderId, ct);
        if (order.Status != OrderStatus.Fulfilled || order.Payment?.CaptureId is null)
        {
            throw new InvalidOperationException($"Order {orderId} is {order.Status} and has no captured payment to refund.");
        }

        var payment = order.Payment;
        var remaining = payment.RefundableAmount;
        var refundAmount = amount ?? remaining;
        if (refundAmount <= 0)
        {
            throw new InvalidOperationException($"Order {orderId} has already been refunded in full.");
        }
        if (refundAmount > remaining)
        {
            throw new InvalidOperationException(
                $"Refund of {refundAmount:F2} exceeds the refundable amount of {remaining:F2} {payment.Currency} for order {orderId}.");
        }

        // An explicit amount equal to the remaining balance is still a legitimate partial call;
        // only a caller that omitted the amount gets PayPal's empty-payload full refund.
        var refund = await _gateway.RefundAsync(payment.CaptureId, amount, payment.Currency,
            $"eshop-{RunId}-{idempotencyKey}", ct);

        var recorded = payment.AddRefund(refund.RefundId, refundAmount, refund.Status, idempotencyKey);
        await _orderRepository.UpdateAsync(order, ct);

        _logger.LogInformation("Order {OrderId} refunded {Amount} via PayPal refund {RefundId}.",
            order.Id, refundAmount, refund.RefundId);
        return (order, recorded, false);
    }

    public async Task<IReadOnlyList<Order>> ListMyOrdersAsync(string buyerId, CancellationToken ct)
    {
        return await _orderRepository.ListAsync(new CustomerOrdersWithPaymentSpecification(buyerId), ct);
    }

    public async Task<SavedPaymentMethod> SaveCardAsync(string buyerId, GatewayCard card, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(card.Number) || string.IsNullOrWhiteSpace(card.Expiry))
        {
            throw new InvalidOperationException("Card number and expiry are required.");
        }

        var last4 = card.Number.Length >= 4 ? card.Number[^4..] : card.Number;
        var saved = await _gateway.SaveCardAsync(buyerId, card,
            $"eshop-savecard-{buyerId}-{last4}-{card.Expiry}", ct);

        var entity = new SavedPaymentMethod(buyerId, saved.VaultTokenId, saved.Brand, saved.LastDigits, saved.Expiry);
        return await _savedCardRepository.AddAsync(entity, ct);
    }

    public async Task<IReadOnlyList<SavedPaymentMethod>> ListCardsAsync(string buyerId, CancellationToken ct)
    {
        return await _savedCardRepository.ListAsync(new SavedPaymentMethodsByBuyerSpecification(buyerId), ct);
    }

    public async Task DeleteCardAsync(string buyerId, int paymentMethodId, CancellationToken ct)
    {
        var savedCard = await _savedCardRepository.GetByIdAsync(paymentMethodId, ct);
        if (savedCard is null || savedCard.BuyerId != buyerId)
        {
            throw new KeyNotFoundException($"Saved payment method {paymentMethodId} was not found.");
        }

        try
        {
            await _gateway.DeleteCardAsync(savedCard.VaultTokenId, ct);
        }
        catch (PaymentGatewayException ex) when (ex.ProviderStatus is >= 400 and < 500)
        {
            // The vaulted token is already gone (or invalid) at PayPal; removing the local
            // record still satisfies the delete.
            _logger.LogWarning("PayPal rejected deletion of vaulted card for buyer {BuyerId}; removing local record anyway.", buyerId);
        }

        await _savedCardRepository.DeleteAsync(savedCard, ct);
    }

    public async Task<ReconciliationReport> ReconcileAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken ct)
    {
        var transactions = await _gateway.SearchTransactionsAsync(from, to, ct);
        var orders = await _orderRepository.ListAsync(new OrdersPaidInRangeSpecification(from, to), ct);
        var ordersByInvoice = orders
            .Where(o => o.Payment is not null)
            .ToDictionary(o => o.Payment!.InvoiceId, o => o);

        var entries = new List<ReconciliationEntry>();
        var matchedInvoiceIds = new HashSet<string>();
        foreach (var transaction in transactions)
        {
            var reference = transaction.InvoiceId ?? transaction.CustomField;
            var matched = reference is not null && ordersByInvoice.ContainsKey(reference);
            if (matched)
            {
                matchedInvoiceIds.Add(reference!);
            }
            entries.Add(new ReconciliationEntry(
                transaction.TransactionId, transaction.InitiatedAt, transaction.Amount, transaction.Currency,
                transaction.Fee, transaction.Status, reference, matched));
        }

        var unmatchedOrders = orders
            .Where(o => o.Payment is not null && !matchedInvoiceIds.Contains(o.Payment.InvoiceId))
            .Select(o => new UnmatchedOrder(o.Id, o.OrderDate, o.Total(), o.Payment!.InvoiceId))
            .ToList();

        return new ReconciliationReport(from, to, entries, unmatchedOrders);
    }

    private static string NewInvoiceId(int orderId) => $"eshop-order-{orderId}-{Guid.NewGuid():N}";

    private async Task<Order> GetOrderAsync(int orderId, CancellationToken ct)
    {
        var order = await _orderRepository.FirstOrDefaultAsync(new OrderWithPaymentSpecification(orderId), ct);
        if (order is null)
        {
            throw new KeyNotFoundException($"Order {orderId} was not found.");
        }
        return order;
    }

    private async Task<Order> GetOwnedOrderAsync(string buyerId, int orderId, CancellationToken ct)
    {
        var order = await GetOrderAsync(orderId, ct);
        if (order.BuyerId != buyerId)
        {
            throw new KeyNotFoundException($"Order {orderId} was not found.");
        }
        return order;
    }
}

public record ReconciliationEntry(
    string TransactionId, string? InitiatedAt, decimal? Amount, string? Currency,
    decimal? Fee, string? Status, string? OrderReference, bool MatchedToOrder);

public record UnmatchedOrder(int OrderId, DateTimeOffset OrderDate, decimal Total, string PaymentReference);

public record ReconciliationReport(
    DateTimeOffset From, DateTimeOffset To,
    IReadOnlyList<ReconciliationEntry> Transactions,
    IReadOnlyList<UnmatchedOrder> OrdersWithoutPayPalTransaction);
