using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Ardalis.Result;
using Microsoft.eShopWeb.ApplicationCore.Entities;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentMethodAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Payments;
using Microsoft.eShopWeb.ApplicationCore.Specifications;

namespace Microsoft.eShopWeb.ApplicationCore.Services;

/// <summary>
/// Coordinates the domain (orders, payments) with the payment gateway (PayPal). Every payment
/// operation is idempotent in effect: existing state is honoured before any call is made, so a
/// double-click never authorizes or captures twice.
/// </summary>
public class PaymentService : IPaymentService
{
    private readonly IRepository<Order> _orderRepository;
    private readonly IRepository<CatalogItem> _itemRepository;
    private readonly IRepository<PaymentMethod> _paymentMethodRepository;
    private readonly IPaymentGateway _gateway;
    private readonly IUriComposer _uriComposer;
    private readonly IPaymentSettings _settings;
    private readonly IAppLogger<PaymentService> _logger;

    public PaymentService(
        IRepository<Order> orderRepository,
        IRepository<CatalogItem> itemRepository,
        IRepository<PaymentMethod> paymentMethodRepository,
        IPaymentGateway gateway,
        IUriComposer uriComposer,
        IPaymentSettings settings,
        IAppLogger<PaymentService> logger)
    {
        _orderRepository = orderRepository;
        _itemRepository = itemRepository;
        _paymentMethodRepository = paymentMethodRepository;
        _gateway = gateway;
        _uriComposer = uriComposer;
        _settings = settings;
        _logger = logger;
    }

    private string Currency => _settings.Currency;

    public async Task<Result<Order>> PlaceOrderAsync(string buyerId, IReadOnlyCollection<OrderLine> lines,
        Address shipToAddress, CancellationToken cancellationToken = default)
    {
        if (lines is null || lines.Count == 0)
            return Result<Order>.Invalid(new List<ValidationError> { new ValidationError { ErrorMessage = "An order must contain at least one item." } });

        if (lines.Any(l => l.Quantity <= 0))
            return Result<Order>.Invalid(new List<ValidationError> { new ValidationError { ErrorMessage = "Every line quantity must be greater than zero." } });

        var ids = lines.Select(l => l.CatalogItemId).Distinct().ToArray();
        var catalogItems = await _itemRepository.ListAsync(new CatalogItemsSpecification(ids), cancellationToken);

        var missing = ids.Where(id => catalogItems.All(c => c.Id != id)).ToArray();
        if (missing.Length > 0)
            return Result<Order>.Invalid(new List<ValidationError> { new ValidationError { ErrorMessage = $"Catalog item(s) not found: {string.Join(", ", missing)}." } });

        var orderItems = lines.Select(line =>
        {
            var catalogItem = catalogItems.First(c => c.Id == line.CatalogItemId);
            var itemOrdered = new CatalogItemOrdered(catalogItem.Id, catalogItem.Name,
                _uriComposer.ComposePicUri(catalogItem.PictureUri));
            return new OrderItem(itemOrdered, catalogItem.Price, line.Quantity);
        }).ToList();

        var order = new Order(buyerId, shipToAddress, orderItems);
        await _orderRepository.AddAsync(order, cancellationToken);

        _logger.LogInformation($"Placed order {order.Id} for {buyerId} totalling {order.Total()} {Currency}.");
        return Result<Order>.Success(order);
    }

    public async Task<Result<Order>> AuthorizeOrderAsync(string buyerId, int orderId, CardDetails? card,
        int? savedPaymentMethodId, CancellationToken cancellationToken = default)
    {
        var order = await _orderRepository.FirstOrDefaultAsync(new OrderWithPaymentByIdSpec(orderId), cancellationToken);
        if (order is null || order.BuyerId != buyerId)
            return Result<Order>.NotFound();

        if (order.Status == OrderStatus.Cancelled)
            return Result<Order>.Error("This order has been cancelled and can no longer be paid.");

        // Idempotent in effect: if the hold already exists, return it rather than authorizing again.
        if (order.Payment is not null)
            return Result<Order>.Success(order);

        if (card is null && savedPaymentMethodId is null)
            return Result<Order>.Invalid(new List<ValidationError> { new ValidationError { ErrorMessage = "Provide either card details or a saved card id to pay with." } });
        if (card is not null && savedPaymentMethodId is not null)
            return Result<Order>.Invalid(new List<ValidationError> { new ValidationError { ErrorMessage = "Provide either card details or a saved card id, not both." } });

        string? vaultId = null;
        if (savedPaymentMethodId is not null)
        {
            var method = await _paymentMethodRepository.FirstOrDefaultAsync(
                new PaymentMethodByIdSpecification(savedPaymentMethodId.Value, buyerId), cancellationToken);
            if (method is null)
                return Result<Order>.NotFound();
            vaultId = method.VaultId;
        }

        var amount = order.Total();
        var request = new PaymentAuthorizationRequest(
            Amount: amount,
            Currency: Currency,
            InvoiceId: InvoiceReference(order),
            CustomId: order.Id.ToString(CultureInfo.InvariantCulture),
            IdempotencyKey: OperationKey("auth", order),
            Card: card,
            VaultId: vaultId);

        AuthorizationResult auth;
        try
        {
            auth = await _gateway.AuthorizeAsync(request, cancellationToken);
        }
        catch (PaymentException ex)
        {
            _logger.LogWarning($"Authorization failed for order {orderId}: {ex.Message}");
            return Result<Order>.Error($"The card could not be authorized: {ex.Message}");
        }

        if (auth.RequiresBrowserApproval)
        {
            return Result<Order>.Error(
                "PayPal returned a challenge that requires the shopper to approve this card in a browser " +
                "(3-D Secure). This integration processes cards without a browser step and cannot complete " +
                "this payment. Ask the shopper to use a different card.");
        }

        var payment = new Payment(amount, Currency);
        payment.RecordAuthorization(auth.PayPalOrderId, auth.AuthorizationId, auth.Status,
            auth.ExpiresAt, savedPaymentMethodId);
        order.SetPayment(payment);
        await _orderRepository.UpdateAsync(order, cancellationToken);

        _logger.LogInformation($"Authorized {amount} {Currency} for order {orderId} (auth {auth.AuthorizationId}).");
        return Result<Order>.Success(order);
    }

    public async Task<Result<Order>> FulfilOrderAsync(int orderId, CancellationToken cancellationToken = default)
    {
        var order = await _orderRepository.FirstOrDefaultAsync(new OrderWithPaymentByIdSpec(orderId), cancellationToken);
        if (order is null)
            return Result<Order>.NotFound();

        var payment = order.Payment;
        if (payment is null || (!payment.IsAuthorized && !payment.IsCaptured))
            return Result<Order>.Error("This order has no authorized payment to capture.");

        // Idempotent in effect: already fulfilled/captured -> return as-is.
        if (order.Status == OrderStatus.Fulfilled && payment.IsCaptured)
            return Result<Order>.Success(order);

        var authorizationId = payment.AuthorizationId!;

        // An authorization that has gone stale before fulfilment must be renewed, not fail the capture.
        try
        {
            var snapshot = await _gateway.GetAuthorizationAsync(authorizationId, cancellationToken);
            var stale = IsStale(snapshot);
            if (stale)
            {
                _logger.LogInformation($"Authorization {authorizationId} for order {orderId} is stale ({snapshot.Status}); renewing.");
                try
                {
                    var renewed = await _gateway.ReauthorizeAsync(authorizationId, payment.Amount, payment.Currency, cancellationToken);
                    payment.RenewAuthorization(renewed.AuthorizationId, renewed.Status, renewed.ExpiresAt);
                    authorizationId = renewed.AuthorizationId;
                    await _orderRepository.UpdateAsync(order, cancellationToken);
                }
                catch (PaymentException ex)
                {
                    _logger.LogWarning($"Reauthorization failed for order {orderId}: {ex.Message}");
                    return Result<Order>.Error(
                        $"The authorization for order {orderId} has expired and could no longer be renewed " +
                        $"({ex.Message}). Money was not captured. Ask the shopper to pay again: cancel this " +
                        "order and place a new one, or take a fresh payment.");
                }
            }
        }
        catch (PaymentException ex)
        {
            _logger.LogWarning($"Could not read authorization {authorizationId} for order {orderId}: {ex.Message}");
            // Fall through and let the capture attempt surface any hard failure below.
        }

        CaptureResult capture;
        try
        {
            capture = await _gateway.CaptureAsync(authorizationId, payment.Amount, payment.Currency,
                InvoiceReference(order), OperationKey("capture", order), cancellationToken);
        }
        catch (PaymentException ex)
        {
            _logger.LogWarning($"Capture failed for order {orderId}: {ex.Message}");
            return Result<Order>.Error(
                $"The held funds for order {orderId} could not be captured ({ex.Message}). " +
                "The order was not fulfilled.");
        }

        payment.RecordCapture(capture.CaptureId, capture.Status, capture.CapturedAmount,
            capture.PayPalFee, capture.NetAmount);
        order.MarkFulfilled();
        await _orderRepository.UpdateAsync(order, cancellationToken);

        _logger.LogInformation($"Captured {capture.CapturedAmount} {Currency} for order {orderId} " +
            $"(capture {capture.CaptureId}, fee {capture.PayPalFee}, net {capture.NetAmount}).");
        return Result<Order>.Success(order);
    }

    public async Task<Result<Order>> CancelOrderAsync(int orderId, CancellationToken cancellationToken = default)
    {
        var order = await _orderRepository.FirstOrDefaultAsync(new OrderWithPaymentByIdSpec(orderId), cancellationToken);
        if (order is null)
            return Result<Order>.NotFound();

        if (order.Status == OrderStatus.Cancelled)
            return Result<Order>.Success(order); // idempotent

        if (order.Status == OrderStatus.Fulfilled)
            return Result<Order>.Error("This order has already been fulfilled; use a refund to return money.");

        var payment = order.Payment;
        if (payment is not null && payment.IsAuthorized)
        {
            try
            {
                await _gateway.VoidAsync(payment.AuthorizationId!, cancellationToken);
            }
            catch (PaymentException ex)
            {
                _logger.LogWarning($"Void failed for order {orderId}: {ex.Message}");
                return Result<Order>.Error($"The held funds for order {orderId} could not be released ({ex.Message}).");
            }
            payment.RecordVoid();
        }

        order.MarkCancelled();
        await _orderRepository.UpdateAsync(order, cancellationToken);

        _logger.LogInformation($"Cancelled order {orderId}; any hold released.");
        return Result<Order>.Success(order);
    }

    public async Task<Result<PaymentRefund>> RefundOrderAsync(string buyerId, int orderId, decimal? amount,
        string idempotencyKey, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(idempotencyKey))
            return Result<PaymentRefund>.Invalid(new List<ValidationError> { new ValidationError { ErrorMessage = "An idempotency key is required for refunds." } });

        var order = await _orderRepository.FirstOrDefaultAsync(new OrderWithPaymentByIdSpec(orderId), cancellationToken);
        if (order is null || order.BuyerId != buyerId)
            return Result<PaymentRefund>.NotFound();

        var payment = order.Payment;
        if (payment is null || !payment.IsCaptured)
            return Result<PaymentRefund>.Error("This order has no captured payment to refund.");

        // Idempotent: a repeat under the same key returns the original refund, never a second one.
        var existing = payment.FindRefundByIdempotencyKey(idempotencyKey);
        if (existing is not null)
            return Result<PaymentRefund>.Success(existing);

        var refundAmount = amount ?? payment.RefundableAmount;
        if (refundAmount <= 0m)
            return Result<PaymentRefund>.Invalid(new List<ValidationError> { new ValidationError { ErrorMessage = "Refund amount must be greater than zero." } });
        if (refundAmount > payment.RefundableAmount)
            return Result<PaymentRefund>.Error(
                $"Refund of {refundAmount} {Currency} exceeds the remaining refundable amount of " +
                $"{payment.RefundableAmount} {Currency} for this order.");

        RefundResult result;
        try
        {
            result = await _gateway.RefundAsync(payment.CaptureId!, refundAmount, payment.Currency,
                idempotencyKey, cancellationToken);
        }
        catch (PaymentException ex)
        {
            _logger.LogWarning($"Refund failed for order {orderId}: {ex.Message}");
            return Result<PaymentRefund>.Error($"The refund could not be processed ({ex.Message}).");
        }

        var refund = new PaymentRefund(idempotencyKey, result.Amount, result.RefundId, result.Status);
        payment.AddRefund(refund);
        await _orderRepository.UpdateAsync(order, cancellationToken);

        _logger.LogInformation($"Refunded {result.Amount} {Currency} for order {orderId} (refund {result.RefundId}).");
        return Result<PaymentRefund>.Success(refund);
    }

    public async Task<IReadOnlyCollection<Order>> GetOrdersForBuyerAsync(string buyerId,
        CancellationToken cancellationToken = default)
    {
        var orders = await _orderRepository.ListAsync(new CustomerOrdersWithPaymentSpecification(buyerId), cancellationToken);
        return orders;
    }

    public async Task<Result<ReconciliationReport>> ReconcileAsync(DateTimeOffset from, DateTimeOffset to,
        CancellationToken cancellationToken = default)
    {
        if (to < from)
            return Result<ReconciliationReport>.Invalid(new List<ValidationError> { new ValidationError { ErrorMessage = "'to' must be on or after 'from'." } });

        IReadOnlyCollection<GatewayTransaction> transactions;
        try
        {
            transactions = await _gateway.ListTransactionsAsync(from, to, cancellationToken);
        }
        catch (PaymentException ex)
        {
            _logger.LogWarning($"Transaction search failed: {ex.Message}");
            return Result<ReconciliationReport>.Error($"PayPal's transaction report could not be retrieved ({ex.Message}).");
        }

        var capturedOrders = await _orderRepository.ListAsync(new CapturedOrdersSpecification(), cancellationToken);

        // Index eShop's captured orders by both the reference we stamp on PayPal and the capture id.
        var byInvoice = capturedOrders.ToDictionary(InvoiceReference, o => o, StringComparer.OrdinalIgnoreCase);
        var byCaptureId = capturedOrders
            .Where(o => o.Payment?.CaptureId is not null)
            .ToDictionary(o => o.Payment!.CaptureId!, o => o, StringComparer.OrdinalIgnoreCase);

        var entries = new List<ReconciliationEntry>();
        var matchedOrderIds = new HashSet<int>();

        foreach (var tx in transactions)
        {
            Order? order = null;
            if (!string.IsNullOrEmpty(tx.InvoiceId))
                byInvoice.TryGetValue(tx.InvoiceId!, out order);
            if (order is null)
                byCaptureId.TryGetValue(tx.TransactionId, out order);

            if (order is not null)
            {
                matchedOrderIds.Add(order.Id);
                entries.Add(new ReconciliationEntry(ReconciliationMatch.Matched, tx.TransactionId, tx.Status,
                    tx.Amount, tx.Currency, tx.InvoiceId, order.Id, order.Payment!.CapturedAmount,
                    order.Payment!.Status.ToString()));
            }
            else
            {
                entries.Add(new ReconciliationEntry(ReconciliationMatch.PayPalOnly, tx.TransactionId, tx.Status,
                    tx.Amount, tx.Currency, tx.InvoiceId, null, null, null));
            }
        }

        foreach (var order in capturedOrders.Where(o => !matchedOrderIds.Contains(o.Id)))
        {
            entries.Add(new ReconciliationEntry(ReconciliationMatch.EShopOnly, null, null, null,
                order.Payment!.Currency, InvoiceReference(order), order.Id, order.Payment!.CapturedAmount,
                order.Payment!.Status.ToString()));
        }

        var report = new ReconciliationReport(
            From: from,
            To: to,
            PayPalTransactionCount: transactions.Count,
            MatchedCount: entries.Count(e => e.Match == ReconciliationMatch.Matched),
            PayPalOnlyCount: entries.Count(e => e.Match == ReconciliationMatch.PayPalOnly),
            EShopOnlyCount: entries.Count(e => e.Match == ReconciliationMatch.EShopOnly),
            Entries: entries);

        return Result<ReconciliationReport>.Success(report);
    }

    // A stable, per-order reference stamped on PayPal so its records line up with eShop orders.
    private static string InvoiceReference(Order order) =>
        $"ESHOP-{order.Id}-{order.OrderDate.UtcTicks}";

    // A deterministic idempotency key: same across a double-click (same order), unique across runs.
    private static string OperationKey(string operation, Order order) =>
        $"eshop-{operation}-{order.Id}-{order.OrderDate.UtcTicks}";

    private static bool IsStale(AuthorizationSnapshot snapshot)
    {
        if (string.Equals(snapshot.Status, "EXPIRED", StringComparison.OrdinalIgnoreCase))
            return true;
        if (snapshot.ExpiresAt is { } expiry && expiry <= DateTimeOffset.UtcNow)
            return true;
        return false;
    }
}
