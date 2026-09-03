using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.eShopWeb.ApplicationCore.Entities;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentMethodAggregate;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Payments;
using Microsoft.eShopWeb.ApplicationCore.Specifications;

namespace Microsoft.eShopWeb.Infrastructure.Payments;

public sealed class OrderPaymentService : IOrderPaymentService
{
    private readonly IRepository<Order> _orderRepository;
    private readonly IRepository<Payment> _paymentRepository;
    private readonly IReadRepository<CatalogItem> _catalogRepository;
    private readonly IReadRepository<SavedPaymentMethod> _savedCardRepository;
    private readonly IPayPalPaymentGateway _gateway;
    private readonly ILogger<OrderPaymentService> _logger;
    private readonly string _currency;

    public OrderPaymentService(
        IRepository<Order> orderRepository,
        IRepository<Payment> paymentRepository,
        IReadRepository<CatalogItem> catalogRepository,
        IReadRepository<SavedPaymentMethod> savedCardRepository,
        IPayPalPaymentGateway gateway,
        IOptions<PayPalSettings> settings,
        ILogger<OrderPaymentService> logger)
    {
        _orderRepository = orderRepository;
        _paymentRepository = paymentRepository;
        _catalogRepository = catalogRepository;
        _savedCardRepository = savedCardRepository;
        _gateway = gateway;
        _logger = logger;
        _currency = settings.Value.Currency;
    }

    public async Task<OrderPaymentSummary> PlaceOrderAsync(string buyerId, IReadOnlyList<OrderLineInput> lines, ShippingAddressInput address, CancellationToken ct = default)
    {
        if (lines is null || lines.Count == 0)
            throw new InvalidPaymentOperationException("An order needs at least one line item.");

        var ids = lines.Select(l => l.CatalogItemId).Distinct().ToArray();
        var catalogItems = await _catalogRepository.ListAsync(new CatalogItemsSpecification(ids), ct);
        var byId = catalogItems.ToDictionary(c => c.Id);

        var items = new List<OrderItem>();
        foreach (var line in lines)
        {
            if (line.Quantity <= 0)
                throw new InvalidPaymentOperationException($"Quantity for catalog item {line.CatalogItemId} must be positive.");
            if (!byId.TryGetValue(line.CatalogItemId, out var catalogItem))
                throw new InvalidPaymentOperationException($"Catalog item {line.CatalogItemId} does not exist.");

            var itemOrdered = new CatalogItemOrdered(catalogItem.Id, catalogItem.Name, catalogItem.PictureUri);
            items.Add(new OrderItem(itemOrdered, catalogItem.Price, line.Quantity));
        }

        var shipTo = new Address(address.Street, address.City, address.State, address.Country, address.ZipCode);
        var order = new Order(buyerId, shipTo, items);
        order = await _orderRepository.AddAsync(order, ct);

        var payment = new Payment(order.Id, buyerId, order.Total(), _currency);
        payment = await _paymentRepository.AddAsync(payment, ct);

        _logger.LogInformation("Placed order {OrderId} for {BuyerId}, total {Total} {Currency}.",
            order.Id, buyerId, order.Total(), _currency);
        return ToSummary(payment);
    }

    public async Task<OrderPaymentSummary> AuthorizeAsync(string buyerId, int orderId, PaymentInstrument instrument, CancellationToken ct = default)
    {
        var payment = await LoadOwnedPaymentAsync(orderId, buyerId, ct);

        // Idempotent in effect: once authorized (or beyond), a repeat does not authorize again.
        if (payment.Status != PaymentStatus.PendingPayment && payment.Status != PaymentStatus.Failed)
            return ToSummary(payment);

        // Resolve the funding instrument.
        string? vaultTokenId = null;
        string description;
        if (instrument.SavedPaymentMethodId is { } savedId)
        {
            var saved = (await _savedCardRepository.ListAsync(new SavedPaymentMethodByIdSpec(savedId, buyerId), ct)).FirstOrDefault()
                ?? throw new PaymentNotFoundException($"Saved card {savedId} was not found for this shopper.");
            vaultTokenId = saved.VaultTokenId;
            description = $"Saved card {saved.CardBrand} ****{saved.LastDigits}".Trim();
        }
        else if (instrument.Card is { } card)
        {
            var last4 = card.Number.Length >= 4 ? card.Number[^4..] : card.Number;
            description = $"Card ****{last4}";
        }
        else
        {
            throw new InvalidPaymentOperationException("Provide card details or a saved card id to pay.");
        }

        var request = new AuthorizeRequest(
            orderId, payment.Amount, payment.CurrencyCode,
            ReferenceId: orderId.ToString(CultureInfo.InvariantCulture),
            Card: instrument.Card,
            VaultTokenId: vaultTokenId,
            IdempotencyKey: $"order-{orderId}");

        try
        {
            var result = await _gateway.AuthorizeAsync(request, ct);
            payment.MarkAuthorized(result.PayPalOrderId, result.AuthorizationId, result.AuthorizationStatus, description);
            await _paymentRepository.UpdateAsync(payment, ct);
            return ToSummary(payment);
        }
        catch (PayPalGatewayException ex)
        {
            payment.MarkFailed(ex.Message);
            await _paymentRepository.UpdateAsync(payment, ct);
            throw;
        }
    }

    public async Task<OrderPaymentSummary> FulfilAsync(int orderId, CancellationToken ct = default)
    {
        var payment = await LoadPaymentAsync(orderId, ct);

        if (payment.Status is PaymentStatus.Captured or PaymentStatus.PartiallyRefunded or PaymentStatus.Refunded)
            return ToSummary(payment); // already fulfilled — idempotent

        if (payment.Status != PaymentStatus.Authorized || payment.AuthorizationId is null)
            throw new InvalidPaymentOperationException(
                $"Order {orderId} cannot be fulfilled from state '{payment.Status}': it must be authorized first.");

        var command = new CaptureCommand(payment.AuthorizationId, payment.Amount, payment.CurrencyCode, $"order-{orderId}");
        var result = await _gateway.CaptureAsync(command, ct);

        if (result.RenewedAuthorizationId is { } renewed)
            payment.UpdateAuthorization(renewed, "CREATED");

        payment.MarkCaptured(result.CaptureId, result.CaptureStatus, result.Gross, result.PayPalFee, result.NetAmount);
        await _paymentRepository.UpdateAsync(payment, ct);

        _logger.LogInformation("Fulfilled order {OrderId}: captured {Gross} (fee {Fee}, net {Net}).",
            orderId, result.Gross, result.PayPalFee, result.NetAmount);
        return ToSummary(payment);
    }

    public async Task<OrderPaymentSummary> CancelAsync(int orderId, CancellationToken ct = default)
    {
        var payment = await LoadPaymentAsync(orderId, ct);

        if (payment.Status == PaymentStatus.Canceled)
            return ToSummary(payment); // idempotent

        if (payment.Status is PaymentStatus.Captured or PaymentStatus.PartiallyRefunded or PaymentStatus.Refunded)
            throw new InvalidPaymentOperationException(
                $"Order {orderId} has already been fulfilled and cannot be cancelled; issue a refund instead.");

        if (payment.Status == PaymentStatus.Authorized && payment.AuthorizationId is not null)
        {
            await _gateway.VoidAsync(payment.AuthorizationId, ct);
        }

        payment.MarkCanceled();
        await _paymentRepository.UpdateAsync(payment, ct);
        _logger.LogInformation("Cancelled order {OrderId}; any held funds were released.", orderId);
        return ToSummary(payment);
    }

    public async Task<(OrderPaymentSummary Summary, string RefundId)> RefundAsync(
        string buyerId, int orderId, decimal? amount, string idempotencyKey, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(idempotencyKey))
            throw new InvalidPaymentOperationException("A refund idempotency key is required.");

        var payment = await LoadOwnedPaymentAsync(orderId, buyerId, ct);

        if (payment.Status is not (PaymentStatus.Captured or PaymentStatus.PartiallyRefunded) || payment.CaptureId is null)
            throw new InvalidPaymentOperationException(
                $"Order {orderId} cannot be refunded from state '{payment.Status}': it must be captured first.");

        // Idempotent in effect: a repeat under the same key returns the original refund.
        var existing = payment.FindRefundByKey(idempotencyKey);
        if (existing is not null)
            return (ToSummary(payment), existing.PayPalRefundId ?? string.Empty);

        decimal refundAmount;
        if (amount is { } requested)
        {
            if (requested <= 0)
                throw new InvalidPaymentOperationException("Refund amount must be positive.");
            if (requested > payment.RefundableAmount)
                throw new InvalidPaymentOperationException(
                    $"Refund of {requested} exceeds the refundable amount {payment.RefundableAmount}.");
            refundAmount = requested;
        }
        else
        {
            if (payment.RefundableAmount <= 0)
                throw new InvalidPaymentOperationException("Nothing remains to refund on this order.");
            refundAmount = payment.RefundableAmount;
        }

        var command = new RefundCommand(payment.CaptureId, refundAmount, payment.CurrencyCode, idempotencyKey);
        var result = await _gateway.RefundAsync(command, ct);

        payment.AddRefund(new PaymentRefund(idempotencyKey, refundAmount, result.RefundId, result.Status));
        await _paymentRepository.UpdateAsync(payment, ct);

        _logger.LogInformation("Refunded {Amount} on order {OrderId}: refund {RefundId}.", refundAmount, orderId, result.RefundId);
        return (ToSummary(payment), result.RefundId);
    }

    public async Task<IReadOnlyList<OrderPaymentSummary>> GetMyOrdersAsync(string buyerId, CancellationToken ct = default)
    {
        var payments = await _paymentRepository.ListAsync(new PaymentsByBuyerIdSpec(buyerId), ct);
        return payments.Select(ToSummary).ToList();
    }

    public async Task<ReconciliationReport> ReconcileAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken ct = default)
    {
        var payPalTransactions = await _gateway.SearchTransactionsAsync(from, to, ct);
        var payments = await _paymentRepository.ListAsync(ct);

        var byCapture = payments.Where(p => p.CaptureId is not null).ToDictionary(p => p.CaptureId!, p => p);
        var refundIdToPayment = payments
            .SelectMany(p => p.Refunds.Where(r => r.PayPalRefundId is not null).Select(r => (r.PayPalRefundId!, p)))
            .GroupBy(x => x.Item1).ToDictionary(g => g.Key, g => g.First().p);
        var byOrder = payments.ToDictionary(p => p.OrderId);

        var lines = new List<ReconciliationLine>();
        var matchedPaymentOrderIds = new HashSet<int>();

        foreach (var tx in payPalTransactions)
        {
            Payment? match = null;
            if (tx.TransactionId is { } txId && (byCapture.TryGetValue(txId, out var byCap) ? (match = byCap) is not null : refundIdToPayment.TryGetValue(txId, out match)))
            {
                // matched by capture id or refund id
            }
            if (match is null && int.TryParse(tx.CustomField ?? tx.InvoiceId?.Split('-').Skip(1).FirstOrDefault(), out var orderId) && byOrder.TryGetValue(orderId, out var byOrd))
            {
                match = byOrd;
            }

            if (match is not null) matchedPaymentOrderIds.Add(match.OrderId);

            lines.Add(new ReconciliationLine(
                match is not null ? "Matched" : "PayPalOnly",
                tx.TransactionId,
                tx.Status,
                tx.Amount,
                tx.CurrencyCode,
                tx.InvoiceId,
                match?.OrderId,
                match is not null ? match.Status.ToString() : null,
                tx.InitiationDate));
        }

        // eShop payments that PayPal's report (for this range) does not show.
        foreach (var payment in payments.Where(p => p.CaptureId is not null && !matchedPaymentOrderIds.Contains(p.OrderId)))
        {
            lines.Add(new ReconciliationLine(
                "EShopOnly",
                payment.CaptureId,
                payment.CaptureStatus,
                payment.CapturedGross,
                payment.CurrencyCode,
                null,
                payment.OrderId,
                payment.Status.ToString(),
                payment.UpdatedAt));
        }

        var matched = lines.Count(l => l.MatchState == "Matched");
        var payPalOnly = lines.Count(l => l.MatchState == "PayPalOnly");
        var eShopOnly = lines.Count(l => l.MatchState == "EShopOnly");
        return new ReconciliationReport(from, to, matched, payPalOnly, eShopOnly, lines);
    }

    // --- helpers ---

    private async Task<Payment> LoadPaymentAsync(int orderId, CancellationToken ct)
    {
        var payment = (await _paymentRepository.ListAsync(new PaymentByOrderIdSpec(orderId), ct)).FirstOrDefault();
        return payment ?? throw new PaymentNotFoundException($"No payment was found for order {orderId}.");
    }

    private async Task<Payment> LoadOwnedPaymentAsync(int orderId, string buyerId, CancellationToken ct)
    {
        var payment = await LoadPaymentAsync(orderId, ct);
        if (!string.Equals(payment.BuyerId, buyerId, StringComparison.Ordinal))
            throw new PaymentNotFoundException($"No payment was found for order {orderId}.");
        return payment;
    }

    private static OrderPaymentSummary ToSummary(Payment p) => new(
        p.OrderId,
        p.CreatedAt,
        p.Amount,
        p.CurrencyCode,
        p.Status.ToString(),
        p.PaymentMethodDescription,
        p.PayPalOrderId,
        p.AuthorizationId,
        p.AuthorizationStatus,
        p.CaptureId,
        p.CaptureStatus,
        p.CapturedGross,
        p.PayPalFee,
        p.NetAmount,
        p.RefundedAmount,
        p.RefundableAmount,
        p.Refunds
            .OrderBy(r => r.CreatedAt)
            .Select(r => new RefundSummary(r.PayPalRefundId, r.Amount, r.Status, r.CreatedAt))
            .ToList());
}
