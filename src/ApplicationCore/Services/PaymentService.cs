using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Ardalis.GuardClauses;
using Microsoft.eShopWeb.ApplicationCore.Entities;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentMethodAggregate;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Payments;
using Microsoft.eShopWeb.ApplicationCore.Specifications;

namespace Microsoft.eShopWeb.ApplicationCore.Services;

public class PaymentService : IPaymentService
{
    private readonly IRepository<Order> _orderRepository;
    private readonly IRepository<CatalogItem> _itemRepository;
    private readonly IRepository<OrderPayment> _paymentRepository;
    private readonly IRepository<SavedPaymentMethod> _savedCardRepository;
    private readonly IPaymentProcessor _processor;
    private readonly IUriComposer _uriComposer;
    private readonly PaymentOptions _options;
    private readonly IAppLogger<PaymentService> _logger;

    public PaymentService(
        IRepository<Order> orderRepository,
        IRepository<CatalogItem> itemRepository,
        IRepository<OrderPayment> paymentRepository,
        IRepository<SavedPaymentMethod> savedCardRepository,
        IPaymentProcessor processor,
        IUriComposer uriComposer,
        PaymentOptions options,
        IAppLogger<PaymentService> logger)
    {
        _orderRepository = orderRepository;
        _itemRepository = itemRepository;
        _paymentRepository = paymentRepository;
        _savedCardRepository = savedCardRepository;
        _processor = processor;
        _uriComposer = uriComposer;
        _options = options;
        _logger = logger;
    }

    public async Task<int> PlaceOrderAsync(string buyerId, IReadOnlyCollection<OrderLineRequest> items,
        Address shipToAddress, CancellationToken ct = default)
    {
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));
        if (items is null || items.Count == 0)
            throw new ArgumentException("An order must contain at least one item.", nameof(items));
        foreach (var line in items)
        {
            if (line.Quantity <= 0)
                throw new ArgumentException($"Quantity for catalog item {line.CatalogItemId} must be positive.");
        }

        var ids = items.Select(i => i.CatalogItemId).Distinct().ToArray();
        var catalogItems = await _itemRepository.ListAsync(new CatalogItemsSpecification(ids), ct);

        var orderItems = new List<OrderItem>();
        foreach (var line in items)
        {
            var catalogItem = catalogItems.FirstOrDefault(c => c.Id == line.CatalogItemId)
                ?? throw new ArgumentException($"Catalog item {line.CatalogItemId} does not exist.");
            var itemOrdered = new CatalogItemOrdered(catalogItem.Id, catalogItem.Name,
                _uriComposer.ComposePicUri(catalogItem.PictureUri));
            orderItems.Add(new OrderItem(itemOrdered, catalogItem.Price, line.Quantity));
        }

        var order = new Order(buyerId, shipToAddress, orderItems);
        await _orderRepository.AddAsync(order, ct);

        var payment = new OrderPayment(order.Id, buyerId, _options.CurrencyCode, order.Total());
        await _paymentRepository.AddAsync(payment, ct);

        _logger.LogInformation("Placed order {0} for buyer with total {1} {2}", order.Id, order.Total(),
            _options.CurrencyCode);
        return order.Id;
    }

    public async Task<OrderPayment> PayAsync(int orderId, string buyerId, CardDetails? card,
        int? savedPaymentMethodId, CancellationToken ct = default)
    {
        var payment = await LoadOwnedPaymentAsync(orderId, buyerId, ct);

        // Idempotency: a double-click never authorizes twice.
        switch (payment.Status)
        {
            case PaymentStatus.Authorized:
            case PaymentStatus.Captured:
            case PaymentStatus.PartiallyRefunded:
            case PaymentStatus.Refunded:
                _logger.LogInformation("Order {0} already paid (status {1}); returning existing state.",
                    orderId, payment.Status);
                return payment;
            case PaymentStatus.Voided:
                throw new PaymentStateException("This order was cancelled and can no longer be paid.");
        }

        string? vaultTokenId = null;
        if (savedPaymentMethodId.HasValue)
        {
            var saved = await _savedCardRepository.GetByIdAsync(savedPaymentMethodId.Value, ct);
            if (saved is null || saved.BuyerId != buyerId)
                throw new PaymentNotFoundException("The specified saved card was not found.");
            vaultTokenId = saved.VaultTokenId;
        }
        else if (card is null)
        {
            throw new ArgumentException("Provide either card details or a saved payment method id to pay.");
        }

        var request = new AuthorizeRequest
        {
            OrderId = orderId,
            CurrencyCode = payment.CurrencyCode,
            Amount = payment.Amount,
            Card = vaultTokenId is null ? card : null,
            VaultTokenId = vaultTokenId,
            IdempotencyKey = $"auth-{payment.PaymentReference}",
            CustomId = payment.PaymentReference,
            InvoiceId = $"eshop-{payment.PaymentReference}"
        };

        AuthorizationResult result;
        try
        {
            result = await _processor.AuthorizeAsync(request, ct);
        }
        catch (PaymentProcessingException)
        {
            payment.MarkFailed();
            await _paymentRepository.UpdateAsync(payment, ct);
            throw;
        }

        payment.MarkAuthorized(result.PayPalOrderId, result.AuthorizationId, result.Status, result.ExpiresAt);
        await _paymentRepository.UpdateAsync(payment, ct);
        _logger.LogInformation("Authorized order {0}: paypalOrder={1} auth={2}", orderId,
            result.PayPalOrderId, result.AuthorizationId);
        return payment;
    }

    public async Task<OrderPayment> FulfilAsync(int orderId, CancellationToken ct = default)
    {
        var payment = await LoadPaymentAsync(orderId, ct);

        if (payment.Status is PaymentStatus.Captured or PaymentStatus.PartiallyRefunded or PaymentStatus.Refunded)
        {
            _logger.LogInformation("Order {0} already fulfilled (status {1}); returning existing state.",
                orderId, payment.Status);
            return payment;
        }
        if (payment.Status != PaymentStatus.Authorized || payment.AuthorizationId is null)
            throw new PaymentStateException(
                $"Order {orderId} cannot be fulfilled from status '{payment.Status}'. It must be authorized first.");

        // Renew a stale hold rather than failing the fulfilment outright.
        if (payment.AuthorizationExpiresAt is { } expiry && expiry <= DateTimeOffset.UtcNow.AddMinutes(1))
        {
            _logger.LogWarning("Authorization for order {0} is stale (expired {1}); attempting renewal.",
                orderId, expiry);
            try
            {
                var reauth = await _processor.ReauthorizeAsync(payment.PayPalOrderId!, payment.AuthorizationId,
                    payment.CurrencyCode, payment.Amount, $"reauth-{payment.PaymentReference}", ct);
                payment.MarkAuthorizationRenewed(reauth.AuthorizationId, reauth.Status, reauth.ExpiresAt);
                await _paymentRepository.UpdateAsync(payment, ct);
            }
            catch (PaymentProcessingException ex)
            {
                // Cannot be renewed — say so in terms an operator can act on.
                throw new PaymentProcessingException(
                    $"The payment hold for order {orderId} has expired and could not be renewed " +
                    $"({ex.Message}). Ask the shopper to pay again before fulfilling.", ex.StatusCode, ex.DebugId, ex);
            }
        }

        var capture = await _processor.CaptureAsync(payment.AuthorizationId!, payment.CurrencyCode,
            payment.Amount, $"capture-{payment.PaymentReference}", ct);
        payment.MarkCaptured(capture.CaptureId, capture.Status, capture.CapturedAmount, capture.PayPalFee,
            capture.NetAmount);
        await _paymentRepository.UpdateAsync(payment, ct);
        _logger.LogInformation("Captured order {0}: capture={1} gross={2} fee={3} net={4}", orderId,
            capture.CaptureId, capture.CapturedAmount, capture.PayPalFee, capture.NetAmount);
        return payment;
    }

    public async Task<OrderPayment> CancelAsync(int orderId, CancellationToken ct = default)
    {
        var payment = await LoadPaymentAsync(orderId, ct);

        if (payment.Status == PaymentStatus.Voided)
            return payment; // idempotent
        if (payment.Status is PaymentStatus.Captured or PaymentStatus.PartiallyRefunded or PaymentStatus.Refunded)
            throw new PaymentStateException(
                $"Order {orderId} has already been fulfilled. Issue a refund instead of cancelling.");
        if (payment.Status != PaymentStatus.Authorized || payment.AuthorizationId is null)
            throw new PaymentStateException(
                $"Order {orderId} has no held funds to release (status '{payment.Status}').");

        await _processor.VoidAsync(payment.AuthorizationId, $"void-{payment.PaymentReference}", ct);
        payment.MarkVoided();
        await _paymentRepository.UpdateAsync(payment, ct);
        _logger.LogInformation("Voided order {0}: released hold on auth {1}", orderId, payment.AuthorizationId);
        return payment;
    }

    public async Task<PaymentRefund> RefundAsync(int orderId, string buyerId, decimal? amount,
        string idempotencyKey, CancellationToken ct = default)
    {
        Guard.Against.NullOrEmpty(idempotencyKey, nameof(idempotencyKey));
        var payment = await LoadOwnedPaymentAsync(orderId, buyerId, ct);

        if (payment.Status is not (PaymentStatus.Captured or PaymentStatus.PartiallyRefunded) || payment.CaptureId is null)
            throw new PaymentStateException(
                $"Order {orderId} cannot be refunded from status '{payment.Status}'. It must be captured first.");

        // Idempotency: repeating a request under the same key must not refund twice.
        var existing = payment.FindRefundByIdempotencyKey(idempotencyKey);
        if (existing is not null)
        {
            _logger.LogInformation("Refund for order {0} under key {1} already exists ({2}); returning it.",
                orderId, idempotencyKey, existing.PayPalRefundId);
            return existing;
        }

        var remaining = payment.RefundableRemaining();
        if (amount.HasValue)
        {
            if (amount.Value <= 0m)
                throw new ArgumentException("Refund amount must be positive.");
            if (amount.Value > remaining)
                throw new PaymentStateException(
                    $"Refund of {amount.Value} exceeds the remaining refundable amount of {remaining}.");
        }
        var effectiveAmount = amount ?? remaining;

        var result = await _processor.RefundAsync(payment.CaptureId, payment.CurrencyCode, amount,
            $"refund-{payment.PaymentReference}-{idempotencyKey}", ct);

        var refund = new PaymentRefund(result.RefundId, result.Amount > 0 ? result.Amount : effectiveAmount,
            idempotencyKey, result.Status ?? "UNKNOWN");
        payment.AddRefund(refund);
        await _paymentRepository.UpdateAsync(payment, ct);
        _logger.LogInformation("Refunded order {0}: refund={1} amount={2} newStatus={3}", orderId,
            result.RefundId, refund.Amount, payment.Status);
        return refund;
    }

    public async Task<IReadOnlyList<OrderWithPayment>> GetOrdersForBuyerAsync(string buyerId,
        CancellationToken ct = default)
    {
        var orders = await _orderRepository.ListAsync(new CustomerOrdersWithItemsSpecification(buyerId), ct);
        var payments = await _paymentRepository.ListAsync(new OrderPaymentsByBuyerSpec(buyerId), ct);
        var byOrder = payments.ToDictionary(p => p.OrderId);

        return orders
            .Select(o => new OrderWithPayment(o, byOrder.TryGetValue(o.Id, out var p) ? p : null))
            .ToList();
    }

    public async Task<ReconciliationReport> ReconcileAsync(DateTimeOffset from, DateTimeOffset to,
        CancellationToken ct = default)
    {
        var transactions = await _processor.SearchTransactionsAsync(from, to, ct);
        var payments = await _paymentRepository.ListAsync(new OrderPaymentsInRangeSpec(from, to), ct);

        // Index eShop payments by every identifier a PayPal transaction might carry.
        var byReference = payments.ToDictionary(p => p.PaymentReference, p => p);
        var byInvoice = payments.ToDictionary(p => $"eshop-{p.PaymentReference}", p => p);
        var byCaptureId = payments.Where(p => p.CaptureId is not null)
            .GroupBy(p => p.CaptureId!).ToDictionary(g => g.Key, g => g.First());

        var matchedPaymentIds = new HashSet<int>();
        var lines = new List<ReconciliationLine>();
        foreach (var t in transactions)
        {
            OrderPayment? match = null;
            if (t.CustomId is not null) byReference.TryGetValue(t.CustomId, out match);
            if (match is null && t.InvoiceId is not null) byInvoice.TryGetValue(t.InvoiceId, out match);
            if (match is null && t.TransactionId is not null) byCaptureId.TryGetValue(t.TransactionId, out match);

            if (match is not null) matchedPaymentIds.Add(match.Id);

            lines.Add(new ReconciliationLine
            {
                TransactionId = t.TransactionId,
                Status = t.Status,
                Amount = t.Amount,
                CurrencyCode = t.CurrencyCode,
                CustomId = t.CustomId,
                InvoiceId = t.InvoiceId,
                InitiationDate = t.InitiationDate,
                Matched = match is not null,
                EShopOrderId = match?.OrderId
            });
        }

        var unmatchedEShop = payments
            .Where(p => !matchedPaymentIds.Contains(p.Id))
            .Select(p => new UnmatchedEShopPayment
            {
                OrderId = p.OrderId,
                PaymentReference = p.PaymentReference,
                Status = p.Status.ToString(),
                Amount = p.Amount,
                PayPalCaptureId = p.CaptureId
            })
            .ToList();

        return new ReconciliationReport
        {
            From = from,
            To = to,
            PayPalTransactions = lines,
            EShopPaymentsWithoutPayPalRecord = unmatchedEShop,
            MatchedCount = lines.Count(l => l.Matched),
            UnmatchedPayPalCount = lines.Count(l => !l.Matched)
        };
    }

    private async Task<OrderPayment> LoadPaymentAsync(int orderId, CancellationToken ct)
    {
        var payment = await _paymentRepository.FirstOrDefaultAsync(new OrderPaymentByOrderIdSpec(orderId), ct);
        return payment ?? throw new PaymentNotFoundException($"No payment found for order {orderId}.");
    }

    private async Task<OrderPayment> LoadOwnedPaymentAsync(int orderId, string buyerId, CancellationToken ct)
    {
        var payment = await LoadPaymentAsync(orderId, ct);
        if (payment.BuyerId != buyerId)
            throw new PaymentNotFoundException($"No payment found for order {orderId}."); // don't reveal existence
        return payment;
    }
}
