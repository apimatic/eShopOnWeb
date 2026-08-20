using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Ardalis.GuardClauses;
using Ardalis.Result;
using Microsoft.eShopWeb.ApplicationCore.Configuration;
using Microsoft.eShopWeb.ApplicationCore.Entities;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentMethodAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Payments;
using Microsoft.eShopWeb.ApplicationCore.Specifications;

namespace Microsoft.eShopWeb.ApplicationCore.Services;

public class OrderPaymentService : IOrderPaymentService
{
    private readonly IRepository<Order> _orderRepository;
    private readonly IRepository<Payment> _paymentRepository;
    private readonly IRepository<CatalogItem> _itemRepository;
    private readonly IRepository<PaymentMethod> _paymentMethodRepository;
    private readonly IPayPalGateway _gateway;
    private readonly IUriComposer _uriComposer;
    private readonly PayPalSettings _settings;
    private readonly IAppLogger<OrderPaymentService> _logger;

    public OrderPaymentService(
        IRepository<Order> orderRepository,
        IRepository<Payment> paymentRepository,
        IRepository<CatalogItem> itemRepository,
        IRepository<PaymentMethod> paymentMethodRepository,
        IPayPalGateway gateway,
        IUriComposer uriComposer,
        PayPalSettings settings,
        IAppLogger<OrderPaymentService> logger)
    {
        _orderRepository = orderRepository;
        _paymentRepository = paymentRepository;
        _itemRepository = itemRepository;
        _paymentMethodRepository = paymentMethodRepository;
        _gateway = gateway;
        _uriComposer = uriComposer;
        _settings = settings;
        _logger = logger;
    }

    private string Currency => Guard.Against.NullOrWhiteSpace(_settings.Currency, "PayPal:Currency");

    public async Task<Result<PaymentDetailsViewModel>> PlaceOrderAsync(
        string buyerId,
        IReadOnlyList<OrderLineRequest> lines,
        ShippingAddressRequest? shipTo,
        CancellationToken cancellationToken)
    {
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));
        if (lines is null || lines.Count == 0)
        {
            return Result<PaymentDetailsViewModel>.Error("An order must contain at least one line item.");
        }
        if (lines.Any(l => l.Quantity <= 0))
        {
            return Result<PaymentDetailsViewModel>.Error("Every order line must have a quantity of at least one.");
        }

        var ids = lines.Select(l => l.CatalogItemId).Distinct().ToArray();
        var catalogItems = await _itemRepository.ListAsync(new CatalogItemsSpecification(ids), cancellationToken);
        var missing = ids.Where(id => catalogItems.All(c => c.Id != id)).ToArray();
        if (missing.Length > 0)
        {
            return Result<PaymentDetailsViewModel>.Error($"Unknown catalog item id(s): {string.Join(", ", missing)}.");
        }

        var orderItems = lines.Select(line =>
        {
            var catalogItem = catalogItems.First(c => c.Id == line.CatalogItemId);
            var itemOrdered = new CatalogItemOrdered(catalogItem.Id, catalogItem.Name, _uriComposer.ComposePicUri(catalogItem.PictureUri));
            return new OrderItem(itemOrdered, catalogItem.Price, line.Quantity);
        }).ToList();

        var address = shipTo is null
            ? new Address("N/A", "N/A", "N/A", "N/A", "N/A")
            : new Address(shipTo.Street, shipTo.City, shipTo.State, shipTo.Country, shipTo.ZipCode);

        var order = new Order(buyerId, address, orderItems);
        order = await _orderRepository.AddAsync(order, cancellationToken);

        var payment = new Payment(order.Id, buyerId, Currency, order.Total());
        payment = await _paymentRepository.AddAsync(payment, cancellationToken);

        return PaymentMapping.ToViewModel(payment, order);
    }

    public async Task<Result<PaymentDetailsViewModel>> AuthorizeAsync(
        string buyerId,
        int orderId,
        PayInstruction instruction,
        CancellationToken cancellationToken)
    {
        var (order, payment, error) = await LoadOwnedAsync(buyerId, orderId, cancellationToken);
        if (error is not null) return Result<PaymentDetailsViewModel>.Error(error);
        if (order is null || payment is null) return Result<PaymentDetailsViewModel>.NotFound();

        // Idempotent in effect: a second pay after a hold is placed returns the existing state.
        switch (payment.Status)
        {
            case PaymentStatus.Authorized:
                return PaymentMapping.ToViewModel(payment, order);
            case PaymentStatus.Captured:
            case PaymentStatus.PartiallyRefunded:
            case PaymentStatus.Refunded:
                return Result<PaymentDetailsViewModel>.Error($"Order {orderId} has already been captured and cannot be paid again.");
            case PaymentStatus.Voided:
                return Result<PaymentDetailsViewModel>.Error($"Order {orderId} was cancelled and cannot be paid.");
        }

        var sourceResult = await ResolvePaymentSourceAsync(buyerId, instruction, cancellationToken);
        if (!sourceResult.IsSuccess) return Result<PaymentDetailsViewModel>.Error(FirstError(sourceResult));

        var idempotencyKey = $"{payment.InvoiceId}-authorize";
        try
        {
            var auth = await _gateway.AuthorizeAsync(payment.Amount, sourceResult.Value, payment.InvoiceId, idempotencyKey, cancellationToken);
            payment.SetPayPalOrder(auth.PayPalOrderId);

            if (auth.RequiresBuyerApproval)
            {
                const string message = "PayPal requires browser-based buyer approval (a 3-D Secure challenge) for this card. " +
                                       "This card cannot be charged without a browser step; ask the shopper to use a different card.";
                payment.MarkFailed(message);
                await _paymentRepository.UpdateAsync(payment, cancellationToken);
                return Result<PaymentDetailsViewModel>.Error(message);
            }

            if (string.IsNullOrEmpty(auth.AuthorizationId))
            {
                var message = $"PayPal did not return an authorization for order {orderId} (order status {auth.OrderStatus}).";
                payment.MarkFailed(message);
                await _paymentRepository.UpdateAsync(payment, cancellationToken);
                return Result<PaymentDetailsViewModel>.Error(message);
            }

            payment.MarkAuthorized(auth.AuthorizationId, auth.AuthorizationStatus ?? "CREATED");
            await _paymentRepository.UpdateAsync(payment, cancellationToken);
            return PaymentMapping.ToViewModel(payment, order);
        }
        catch (PayPalException ex)
        {
            _logger.LogWarning($"Authorize failed for order {orderId}: {ex.Message}");
            payment.MarkFailed(ex.Message);
            await _paymentRepository.UpdateAsync(payment, cancellationToken);
            return Result<PaymentDetailsViewModel>.Error($"The payment could not be authorized: {ex.Message}");
        }
    }

    public async Task<Result<PaymentDetailsViewModel>> FulfilAsync(int orderId, CancellationToken cancellationToken)
    {
        var (order, payment) = await LoadAsync(orderId, cancellationToken);
        if (order is null || payment is null) return Result<PaymentDetailsViewModel>.NotFound();

        switch (payment.Status)
        {
            case PaymentStatus.Captured:
            case PaymentStatus.PartiallyRefunded:
            case PaymentStatus.Refunded:
                return PaymentMapping.ToViewModel(payment, order); // already fulfilled — idempotent
            case PaymentStatus.Voided:
                return Result<PaymentDetailsViewModel>.Error($"Order {orderId} was cancelled and cannot be fulfilled.");
            case PaymentStatus.AwaitingPayment:
            case PaymentStatus.Failed:
                return Result<PaymentDetailsViewModel>.Error($"Order {orderId} has no active authorization to capture; it must be paid first.");
        }

        var authorizationId = payment.AuthorizationId!;
        try
        {
            var capture = await _gateway.CaptureAsync(authorizationId, $"{payment.InvoiceId}-capture", cancellationToken);
            payment.MarkCaptured(capture.CaptureId, capture.Status, capture.CapturedAmount, capture.PayPalFee, capture.NetAmount);
            await _paymentRepository.UpdateAsync(payment, cancellationToken);
            return PaymentMapping.ToViewModel(payment, order);
        }
        catch (PayPalException captureEx) when (captureEx.IsProviderRejection)
        {
            // A hold that has gone stale before fulfilment is renewed rather than failing the fulfilment outright.
            _logger.LogWarning($"Capture rejected for order {orderId}; attempting to renew the authorization. {captureEx.Message}");
            return await RenewAndCaptureAsync(order, payment, captureEx, cancellationToken);
        }
        catch (PayPalException ex)
        {
            payment.RecordError(ex.Message);
            await _paymentRepository.UpdateAsync(payment, cancellationToken);
            return Result<PaymentDetailsViewModel>.Error($"The order could not be fulfilled: {ex.Message}");
        }
    }

    private async Task<Result<PaymentDetailsViewModel>> RenewAndCaptureAsync(
        Order order, Payment payment, PayPalException captureEx, CancellationToken cancellationToken)
    {
        ReauthorizeResult renewed;
        try
        {
            renewed = await _gateway.ReauthorizeAsync(payment.AuthorizationId!, payment.Amount, $"{payment.InvoiceId}-reauthorize", cancellationToken);
        }
        catch (PayPalException reauthEx)
        {
            var message = $"Order {order.Id} cannot be fulfilled: its payment authorization has expired and can no longer be renewed " +
                          $"({reauthEx.Message}). Cancel the order and ask the shopper to pay again.";
            payment.RecordError(message);
            await _paymentRepository.UpdateAsync(payment, cancellationToken);
            return Result<PaymentDetailsViewModel>.Error(message);
        }

        payment.MarkAuthorizationRenewed(renewed.AuthorizationId, renewed.Status);
        await _paymentRepository.UpdateAsync(payment, cancellationToken);

        try
        {
            var capture = await _gateway.CaptureAsync(renewed.AuthorizationId, $"{payment.InvoiceId}-capture-renewed", cancellationToken);
            payment.MarkCaptured(capture.CaptureId, capture.Status, capture.CapturedAmount, capture.PayPalFee, capture.NetAmount);
            await _paymentRepository.UpdateAsync(payment, cancellationToken);
            return PaymentMapping.ToViewModel(payment, order);
        }
        catch (PayPalException ex)
        {
            var message = $"Order {order.Id}'s authorization was renewed but the capture still failed ({ex.Message}). Original error: {captureEx.Message}.";
            payment.RecordError(message);
            await _paymentRepository.UpdateAsync(payment, cancellationToken);
            return Result<PaymentDetailsViewModel>.Error(message);
        }
    }

    public async Task<Result<PaymentDetailsViewModel>> CancelAsync(int orderId, CancellationToken cancellationToken)
    {
        var (order, payment) = await LoadAsync(orderId, cancellationToken);
        if (order is null || payment is null) return Result<PaymentDetailsViewModel>.NotFound();

        switch (payment.Status)
        {
            case PaymentStatus.Voided:
                return PaymentMapping.ToViewModel(payment, order); // already cancelled — idempotent
            case PaymentStatus.Captured:
            case PaymentStatus.PartiallyRefunded:
            case PaymentStatus.Refunded:
                return Result<PaymentDetailsViewModel>.Error($"Order {orderId} has already been fulfilled; refund it instead of cancelling.");
        }

        // Nothing is held yet (awaiting payment or a failed attempt): just close the order locally.
        if (payment.Status is PaymentStatus.AwaitingPayment or PaymentStatus.Failed || payment.AuthorizationId is null)
        {
            payment.MarkVoided();
            await _paymentRepository.UpdateAsync(payment, cancellationToken);
            return PaymentMapping.ToViewModel(payment, order);
        }

        try
        {
            await _gateway.VoidAsync(payment.AuthorizationId, $"{payment.InvoiceId}-void", cancellationToken);
            payment.MarkVoided();
            await _paymentRepository.UpdateAsync(payment, cancellationToken);
            return PaymentMapping.ToViewModel(payment, order);
        }
        catch (PayPalException ex)
        {
            payment.RecordError(ex.Message);
            await _paymentRepository.UpdateAsync(payment, cancellationToken);
            return Result<PaymentDetailsViewModel>.Error($"The order could not be cancelled: {ex.Message}");
        }
    }

    public async Task<Result<RefundViewModel>> RefundAsync(
        string buyerId, int orderId, decimal? amount, string idempotencyKey, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(idempotencyKey))
        {
            return Result<RefundViewModel>.Error("A refund idempotency key is required.");
        }

        var (order, payment, error) = await LoadOwnedAsync(buyerId, orderId, cancellationToken);
        if (error is not null) return Result<RefundViewModel>.Error(error);
        if (order is null || payment is null) return Result<RefundViewModel>.NotFound();

        if (payment.Status is not (PaymentStatus.Captured or PaymentStatus.PartiallyRefunded))
        {
            return Result<RefundViewModel>.Error($"Order {orderId} has no captured payment to refund.");
        }

        // Repeating a request under the same key must not refund twice.
        if (payment.TryGetRefundByKey(idempotencyKey, out var existing) && existing is not null)
        {
            return new RefundViewModel(existing.PayPalRefundId, existing.Amount, existing.Status, existing.CreatedDate);
        }

        var remaining = payment.RefundableRemaining();
        var refundAmount = amount ?? remaining;
        if (refundAmount <= 0m)
        {
            return Result<RefundViewModel>.Error($"Order {orderId} has nothing left to refund.");
        }
        if (refundAmount > remaining)
        {
            return Result<RefundViewModel>.Error(
                $"Refund of {Format(refundAmount)} exceeds the {Format(remaining)} still refundable on order {orderId}.");
        }

        try
        {
            // Namespace the caller's key by this order instance so the same key never collides with a
            // different order's refund at PayPal, while local de-duplication still keys on the raw value.
            var refund = await _gateway.RefundAsync(payment.CaptureId!, refundAmount, $"{payment.InvoiceId}-{idempotencyKey}", cancellationToken);
            var recorded = payment.AddRefund(idempotencyKey, refundAmount, refund.RefundId, refund.Status);
            await _paymentRepository.UpdateAsync(payment, cancellationToken);
            return new RefundViewModel(recorded.PayPalRefundId, recorded.Amount, recorded.Status, recorded.CreatedDate);
        }
        catch (PayPalException ex)
        {
            payment.RecordError(ex.Message);
            await _paymentRepository.UpdateAsync(payment, cancellationToken);
            return Result<RefundViewModel>.Error($"The refund could not be processed: {ex.Message}");
        }
    }

    public async Task<IReadOnlyList<PaymentDetailsViewModel>> GetOrdersForBuyerAsync(string buyerId, CancellationToken cancellationToken)
    {
        var payments = await _paymentRepository.ListAsync(new PaymentsByBuyerSpec(buyerId), cancellationToken);
        var views = new List<PaymentDetailsViewModel>(payments.Count);
        foreach (var payment in payments)
        {
            var order = await _orderRepository.FirstOrDefaultAsync(new OrderWithItemsByIdSpec(payment.OrderId), cancellationToken);
            if (order is null) continue;
            views.Add(PaymentMapping.ToViewModel(payment, order));
        }
        return views;
    }

    public async Task<Result<ReconciliationReport>> ReconcileAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken)
    {
        if (to < from)
        {
            return Result<ReconciliationReport>.Error("'to' must be on or after 'from'.");
        }

        IReadOnlyList<PayPalTransactionRecord> transactions;
        try
        {
            transactions = await _gateway.SearchTransactionsAsync(from, to, cancellationToken);
        }
        catch (PayPalException ex)
        {
            return Result<ReconciliationReport>.Error($"PayPal transaction search failed: {ex.Message}");
        }

        var payments = await _paymentRepository.ListAsync(cancellationToken);

        // eShop's own record of money movement: one entry per capture and one per refund.
        var eShopEntries = new List<EShopRecord>();
        foreach (var payment in payments)
        {
            if (!string.IsNullOrEmpty(payment.CaptureId) && payment.CapturedAmount is { } captured)
            {
                eShopEntries.Add(new EShopRecord(payment.OrderId, "capture", payment.CaptureId!, captured,
                    payment.CaptureStatus ?? payment.Status.ToString(), payment.LastUpdatedDate, payment.InvoiceId));
            }
            foreach (var refund in payment.Refunds)
            {
                eShopEntries.Add(new EShopRecord(payment.OrderId, "refund", refund.PayPalRefundId, refund.Amount,
                    refund.Status, refund.CreatedDate, payment.InvoiceId));
            }
        }

        var matched = new List<ReconciliationMatch>();
        var onlyInPayPal = new List<PayPalTransactionRecord>();
        var matchedRecords = new HashSet<EShopRecord>();

        foreach (var txn in transactions)
        {
            var record = eShopEntries.FirstOrDefault(r =>
                string.Equals(r.Reference, txn.TransactionId, StringComparison.OrdinalIgnoreCase) ||
                (!string.IsNullOrEmpty(txn.InvoiceId) && string.Equals(r.InvoiceId, txn.InvoiceId, StringComparison.OrdinalIgnoreCase)));

            if (record is not null)
            {
                matched.Add(new ReconciliationMatch(txn.TransactionId, txn.Status, txn.Amount, record.OrderId, record.Kind, record.Amount, record.Status));
                matchedRecords.Add(record);
            }
            else
            {
                onlyInPayPal.Add(txn);
            }
        }

        var onlyInEShop = eShopEntries
            .Where(r => !matchedRecords.Contains(r) && r.Timestamp >= from && r.Timestamp <= to)
            .Select(r => new ReconciliationEShopEntry(r.OrderId, r.Kind, r.Reference, r.Amount, r.Status))
            .ToList();

        var report = new ReconciliationReport(from, to, transactions.Count, matched, onlyInPayPal, onlyInEShop);
        return Result<ReconciliationReport>.Success(report);
    }

    private sealed record EShopRecord(int OrderId, string Kind, string Reference, decimal Amount, string Status, DateTimeOffset Timestamp, string InvoiceId);

    // ---- helpers ----

    private async Task<(Order? order, Payment? payment)> LoadAsync(int orderId, CancellationToken ct)
    {
        var order = await _orderRepository.FirstOrDefaultAsync(new OrderWithItemsByIdSpec(orderId), ct);
        var payment = await _paymentRepository.FirstOrDefaultAsync(new PaymentByOrderIdSpec(orderId), ct);
        return (order, payment);
    }

    private async Task<(Order? order, Payment? payment, string? error)> LoadOwnedAsync(string buyerId, int orderId, CancellationToken ct)
    {
        var (order, payment) = await LoadAsync(orderId, ct);
        if (order is null || payment is null) return (null, null, null);
        if (!string.Equals(order.BuyerId, buyerId, StringComparison.Ordinal) ||
            !string.Equals(payment.BuyerId, buyerId, StringComparison.Ordinal))
        {
            // Do not reveal another shopper's order; treat as not found for this caller.
            return (null, null, $"Order {orderId} was not found.");
        }
        return (order, payment, null);
    }

    private async Task<Result<CardPaymentSource>> ResolvePaymentSourceAsync(string buyerId, PayInstruction instruction, CancellationToken ct)
    {
        var hasCard = instruction.Card is not null;
        var hasSaved = instruction.PaymentMethodId is not null;
        if (hasCard == hasSaved)
        {
            return Result<CardPaymentSource>.Error("Provide exactly one of a card or a saved paymentMethodId to pay with.");
        }

        if (hasSaved)
        {
            var pm = await _paymentMethodRepository.FirstOrDefaultAsync(new PaymentMethodByIdSpec(instruction.PaymentMethodId!.Value), ct);
            if (pm is null || !string.Equals(pm.BuyerId, buyerId, StringComparison.Ordinal))
            {
                return Result<CardPaymentSource>.Error($"Saved card {instruction.PaymentMethodId} was not found.");
            }
            return Result<CardPaymentSource>.Success(CardPaymentSource.Vaulted(pm.PayPalVaultId));
        }

        return Result<CardPaymentSource>.Success(CardPaymentSource.Raw(instruction.Card!));
    }

    private string Format(decimal amount) => amount.ToString("0.00", CultureInfo.InvariantCulture);

    private static string FirstError(IResult result) =>
        result.Errors?.FirstOrDefault() ?? "Invalid request.";
}
