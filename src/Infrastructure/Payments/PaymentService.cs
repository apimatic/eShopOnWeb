using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.eShopWeb.ApplicationCore.Entities;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentMethodAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Payments;
using Microsoft.eShopWeb.ApplicationCore.Specifications;
using Microsoft.Extensions.Logging;

namespace Microsoft.eShopWeb.Infrastructure.Payments;

public sealed class PaymentService : IPaymentService
{
    private readonly IRepository<Order> _orderRepository;
    private readonly IReadRepository<CatalogItem> _itemRepository;
    private readonly IRepository<Payment> _paymentRepository;
    private readonly IReadRepository<SavedPaymentMethod> _savedCardRepository;
    private readonly IUriComposer _uriComposer;
    private readonly IPaymentGateway _gateway;
    private readonly IPaymentConfiguration _configuration;
    private readonly ILogger<PaymentService> _logger;

    public PaymentService(
        IRepository<Order> orderRepository,
        IReadRepository<CatalogItem> itemRepository,
        IRepository<Payment> paymentRepository,
        IReadRepository<SavedPaymentMethod> savedCardRepository,
        IUriComposer uriComposer,
        IPaymentGateway gateway,
        IPaymentConfiguration configuration,
        ILogger<PaymentService> logger)
    {
        _orderRepository = orderRepository;
        _itemRepository = itemRepository;
        _paymentRepository = paymentRepository;
        _savedCardRepository = savedCardRepository;
        _uriComposer = uriComposer;
        _gateway = gateway;
        _configuration = configuration;
        _logger = logger;
    }

    // ---------------------------------------------------------------- Place

    public async Task<int> PlaceOrderAsync(string buyerId, IReadOnlyList<OrderLineInput> items, ShippingAddressInput shipTo, CancellationToken cancellationToken)
    {
        if (items is null || items.Count == 0)
            throw new PaymentValidationException("An order must contain at least one item.");
        if (items.Any(i => i.Quantity <= 0))
            throw new PaymentValidationException("Item quantities must be greater than zero.");

        var ids = items.Select(i => i.CatalogItemId).Distinct().ToArray();
        var catalogItems = await _itemRepository.ListAsync(new CatalogItemsSpecification(ids), cancellationToken);

        var orderItems = new List<OrderItem>();
        foreach (var line in items)
        {
            var catalogItem = catalogItems.FirstOrDefault(c => c.Id == line.CatalogItemId)
                ?? throw new PaymentValidationException($"Catalog item {line.CatalogItemId} does not exist.");

            var itemOrdered = new CatalogItemOrdered(catalogItem.Id, catalogItem.Name, _uriComposer.ComposePicUri(catalogItem.PictureUri));
            orderItems.Add(new OrderItem(itemOrdered, catalogItem.Price, line.Quantity));
        }

        var address = new Address(shipTo.Street, shipTo.City, shipTo.State ?? string.Empty, shipTo.Country, shipTo.ZipCode);
        var order = new Order(buyerId, address, orderItems);
        await _orderRepository.AddAsync(order, cancellationToken);

        var payment = new Payment(order.Id, buyerId, _configuration.CurrencyCode, order.Total());
        await _paymentRepository.AddAsync(payment, cancellationToken);

        _logger.LogInformation("Placed order {OrderId} awaiting payment, total {Total} {Currency}.",
            order.Id, order.Total(), _configuration.CurrencyCode);
        return order.Id;
    }

    // ---------------------------------------------------------------- Pay (authorize)

    public async Task<PaymentView> PayAsync(string buyerId, int orderId, PayInstruction instruction, CancellationToken cancellationToken)
    {
        var payment = await LoadOwnedPaymentAsync(orderId, buyerId, cancellationToken);

        // Idempotent per order: a hold already placed (or beyond) is returned as-is.
        if (payment.Status is PaymentStatus.Authorized or PaymentStatus.Fulfilled
            or PaymentStatus.PartiallyRefunded or PaymentStatus.Refunded)
        {
            return ToView(payment);
        }
        if (payment.Status == PaymentStatus.Cancelled)
            throw new PaymentConflictException($"Order {orderId} was cancelled and cannot be paid.");

        var command = await BuildAuthorizeCommandAsync(buyerId, orderId, payment, instruction, cancellationToken);

        try
        {
            var result = await WithUnknownReplay(() => _gateway.AuthorizeAsync(command, cancellationToken), "authorize");
            payment.MarkAuthorized(result.PayPalOrderId, result.AuthorizationId, result.AuthorizationStatus, result.ExpiresAt);
            await SaveWithConcurrencyGuard(orderId, cancellationToken);
            _logger.LogInformation("Authorized order {OrderId}: paypalOrder={PayPalOrderId} auth={AuthorizationId} status={Status}.",
                orderId, result.PayPalOrderId, result.AuthorizationId, result.AuthorizationStatus);
        }
        catch (PaymentGatewayException ex) when (ex.Kind == PaymentGatewayFailureKind.CallerError)
        {
            payment.MarkFailed(Truncate(ex.Message, 1024));
            await TrySaveAsync(cancellationToken);
            throw;
        }

        return ToView(payment);
    }

    private async Task<AuthorizeCommand> BuildAuthorizeCommandAsync(string buyerId, int orderId, Payment payment, PayInstruction instruction, CancellationToken cancellationToken)
    {
        var reference = payment.ReconciliationReference;
        var description = $"eShopOnWeb order {orderId}";

        if (instruction.SavedPaymentMethodId.HasValue)
        {
            var card = await _savedCardRepository.FirstOrDefaultAsync(
                new SavedPaymentMethodByIdSpecification(instruction.SavedPaymentMethodId.Value), cancellationToken);
            if (card is null || card.BuyerId != buyerId)
                throw new PaymentNotFoundException($"Saved card {instruction.SavedPaymentMethodId} was not found.");

            payment.BeginAuthorization($"{card.Brand} ****{card.LastDigits}");
            return new AuthorizeCommand
            {
                OrderId = orderId,
                Amount = payment.Amount,
                CurrencyCode = payment.CurrencyCode,
                ReconciliationReference = reference,
                Description = description,
                IdempotencyKey = payment.AuthorizeRequestKey,
                VaultId = card.VaultId
            };
        }

        if (instruction.Card is not null)
        {
            var card = instruction.Card;
            if (string.IsNullOrWhiteSpace(card.Number) || string.IsNullOrWhiteSpace(card.Expiry))
                throw new PaymentValidationException("Card number and expiry (YYYY-MM) are required.");

            var last4 = card.Number.Length >= 4 ? card.Number[^4..] : string.Empty;
            payment.BeginAuthorization($"card ****{last4}");
            return new AuthorizeCommand
            {
                OrderId = orderId,
                Amount = payment.Amount,
                CurrencyCode = payment.CurrencyCode,
                ReconciliationReference = reference,
                Description = description,
                IdempotencyKey = payment.AuthorizeRequestKey,
                Card = card
            };
        }

        throw new PaymentValidationException("Provide either card details or a saved payment method id.");
    }

    // ---------------------------------------------------------------- Fulfil (capture)

    public async Task<PaymentView> FulfilAsync(int orderId, CancellationToken cancellationToken)
    {
        var payment = await LoadPaymentAsync(orderId, cancellationToken);

        if (payment.Status == PaymentStatus.Fulfilled)
            return ToView(payment);
        if (payment.Status != PaymentStatus.Authorized)
            throw new PaymentConflictException($"Order {orderId} must be authorized to fulfil (current: {payment.Status}).");

        var authorizationId = payment.AuthorizationId
            ?? throw new PaymentConflictException($"Order {orderId} has no authorization to capture.");

        // Renew a stale hold rather than failing fulfilment outright.
        var info = await _gateway.GetAuthorizationAsync(authorizationId, cancellationToken);
        var expired = info.ExpiresAt.HasValue && info.ExpiresAt.Value <= DateTimeOffset.UtcNow;
        var capturable = info.Status is "CREATED" or "PENDING" or "PARTIALLY_CAPTURED";

        if (!capturable || expired)
        {
            _logger.LogInformation("Authorization {AuthorizationId} for order {OrderId} is stale (status={Status}, expired={Expired}); renewing.",
                authorizationId, orderId, info.Status, expired);
            try
            {
                var renewed = await WithUnknownReplay(
                    () => _gateway.ReauthorizeAsync(authorizationId, payment.Amount, payment.CurrencyCode, payment.ReauthorizeRequestKey, cancellationToken),
                    "reauthorize");
                payment.RenewAuthorization(renewed.AuthorizationId, renewed.Status, renewed.ExpiresAt);
                await TrySaveAsync(cancellationToken);
                authorizationId = renewed.AuthorizationId;
            }
            catch (PaymentGatewayException ex)
            {
                throw new PaymentConflictException(
                    $"The authorization for order {orderId} has expired and could not be renewed " +
                    $"({ex.ProviderErrorName ?? ex.Message}). A new authorization is required — the shopper must pay again.");
            }
        }

        var capture = await WithUnknownReplay(
            () => _gateway.CaptureAsync(authorizationId, payment.Amount, payment.CurrencyCode, payment.CaptureRequestKey, cancellationToken),
            "capture");
        payment.MarkFulfilled(capture.CaptureId, capture.Status, capture.CapturedAmount, capture.PayPalFee, capture.NetAmount);
        await SaveWithConcurrencyGuard(orderId, cancellationToken);

        _logger.LogInformation("Fulfilled order {OrderId}: capture={CaptureId} amount={Amount} fee={Fee} net={Net}.",
            orderId, capture.CaptureId, capture.CapturedAmount, capture.PayPalFee, capture.NetAmount);
        return ToView(payment);
    }

    // ---------------------------------------------------------------- Cancel (void)

    public async Task<PaymentView> CancelAsync(int orderId, CancellationToken cancellationToken)
    {
        var payment = await LoadPaymentAsync(orderId, cancellationToken);

        if (payment.Status == PaymentStatus.Cancelled)
            return ToView(payment);
        if (payment.Status != PaymentStatus.Authorized)
            throw new PaymentConflictException($"Only an authorized (not yet fulfilled) order can be cancelled (current: {payment.Status}).");

        var authorizationId = payment.AuthorizationId
            ?? throw new PaymentConflictException($"Order {orderId} has no authorization to void.");

        await WithUnknownReplay(async () =>
        {
            await _gateway.VoidAsync(authorizationId, payment.VoidRequestKey, cancellationToken);
            return true;
        }, "void");

        payment.MarkCancelled();
        await SaveWithConcurrencyGuard(orderId, cancellationToken);
        _logger.LogInformation("Cancelled order {OrderId}: released authorization {AuthorizationId}.", orderId, authorizationId);
        return ToView(payment);
    }

    // ---------------------------------------------------------------- Refund

    public async Task<(string RefundId, PaymentView Payment)> RefundAsync(string buyerId, int orderId, decimal? amount, string idempotencyKey, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(idempotencyKey))
            throw new PaymentValidationException("A refund idempotency key is required.");

        var payment = await LoadOwnedPaymentAsync(orderId, buyerId, cancellationToken);

        // Idempotent: repeating a request under the same key returns the earlier refund.
        var already = payment.FindRefundByKey(idempotencyKey);
        if (already is not null)
            return (already.PayPalRefundId, ToView(payment));

        if (payment.Status is not (PaymentStatus.Fulfilled or PaymentStatus.PartiallyRefunded))
            throw new PaymentConflictException($"Order {orderId} cannot be refunded (current: {payment.Status}).");
        if (payment.CaptureId is null)
            throw new PaymentConflictException($"Order {orderId} has no captured payment to refund.");

        var remaining = payment.RefundableRemaining();
        if (remaining <= 0m)
            throw new PaymentConflictException($"Order {orderId} has nothing left to refund.");

        var refundAmount = amount ?? remaining;
        if (refundAmount <= 0m)
            throw new PaymentValidationException("Refund amount must be greater than zero.");
        if (refundAmount > remaining)
            throw new PaymentValidationException($"Refund of {refundAmount:0.00} exceeds the refundable remaining {remaining:0.00}.");

        // The raw caller key is stored locally for per-order dedup; the PayPal-Request-Id is namespaced
        // by the payment so the same caller key reused on a different order (or run) never false-collides.
        var providerKey = $"eshop-refund-{payment.PublicId:N}-{idempotencyKey}";
        var result = await WithUnknownReplay(
            () => _gateway.RefundAsync(payment.CaptureId!, refundAmount, payment.CurrencyCode, providerKey, cancellationToken),
            "refund");

        try
        {
            payment.AddRefund(idempotencyKey, result.RefundId, result.Amount, result.Status ?? "COMPLETED");
            await _paymentRepository.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException)
        {
            // A concurrent request under the same key already recorded the refund.
            var reloaded = await LoadPaymentAsync(orderId, cancellationToken);
            var existing = reloaded.FindRefundByKey(idempotencyKey);
            if (existing is not null)
                return (existing.PayPalRefundId, ToView(reloaded));
            throw;
        }

        _logger.LogInformation("Refunded order {OrderId}: refund={RefundId} amount={Amount} (remaining {Remaining}).",
            orderId, result.RefundId, result.Amount, payment.RefundableRemaining());
        return (result.RefundId, ToView(payment));
    }

    // ---------------------------------------------------------------- My orders

    public async Task<IReadOnlyList<OrderPaymentView>> GetMyOrdersAsync(string buyerId, CancellationToken cancellationToken)
    {
        var orders = await _orderRepository.ListAsync(new CustomerOrdersWithItemsSpecification(buyerId), cancellationToken);
        var payments = await _paymentRepository.ListAsync(new PaymentsByBuyerSpecification(buyerId), cancellationToken);
        var paymentsByOrder = payments.ToDictionary(p => p.OrderId);

        var views = new List<OrderPaymentView>();
        foreach (var order in orders.OrderByDescending(o => o.OrderDate))
        {
            paymentsByOrder.TryGetValue(order.Id, out var payment);
            views.Add(new OrderPaymentView
            {
                OrderId = order.Id,
                OrderDate = order.OrderDate,
                Total = order.Total(),
                Items = order.OrderItems.Select(i => new OrderLineView
                {
                    CatalogItemId = i.ItemOrdered.CatalogItemId,
                    ProductName = i.ItemOrdered.ProductName,
                    UnitPrice = i.UnitPrice,
                    Units = i.Units
                }).ToList(),
                Payment = payment is not null
                    ? ToView(payment)
                    : new PaymentView { OrderId = order.Id, Status = PaymentStatus.AwaitingPayment.ToString(), CurrencyCode = _configuration.CurrencyCode, Amount = order.Total() }
            });
        }
        return views;
    }

    // ---------------------------------------------------------------- Reconciliation

    public async Task<ReconciliationResult> ReconcileAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken)
    {
        var report = await _gateway.SearchTransactionsAsync(from, to, cancellationToken);

        var payments = await _paymentRepository.ListAsync(cancellationToken);
        var eShopByReference = payments
            .Where(p => p.PayPalOrderId is not null)
            .GroupBy(p => p.ReconciliationReference)
            .ToDictionary(g => g.Key, g => g.First());

        var lines = new List<ReconciliationLine>();
        var matchedReferences = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var tx in report.Transactions)
        {
            var reference = FirstNonEmpty(tx.CustomField, tx.InvoiceId);
            if (reference is not null && eShopByReference.TryGetValue(reference, out var payment))
            {
                matchedReferences.Add(reference);
                lines.Add(new ReconciliationLine
                {
                    Match = ReconciliationMatch.Matched,
                    OrderId = payment.OrderId,
                    Reference = reference,
                    PayPalTransactionId = tx.TransactionId,
                    PayPalStatus = tx.Status,
                    EventCode = tx.EventCode,
                    PayPalAmount = tx.Amount,
                    CurrencyCode = tx.CurrencyCode,
                    EShopAmount = payment.Amount,
                    EShopPaymentStatus = payment.Status.ToString(),
                    PayPalDate = tx.InitiationDate
                });
            }
            else
            {
                lines.Add(new ReconciliationLine
                {
                    Match = ReconciliationMatch.PayPalOnly,
                    Reference = reference,
                    PayPalTransactionId = tx.TransactionId,
                    PayPalStatus = tx.Status,
                    EventCode = tx.EventCode,
                    PayPalAmount = tx.Amount,
                    CurrencyCode = tx.CurrencyCode,
                    PayPalDate = tx.InitiationDate
                });
            }
        }

        foreach (var (reference, payment) in eShopByReference)
        {
            if (matchedReferences.Contains(reference)) continue;
            lines.Add(new ReconciliationLine
            {
                Match = ReconciliationMatch.EShopOnly,
                OrderId = payment.OrderId,
                Reference = reference,
                EShopAmount = payment.Amount,
                CurrencyCode = payment.CurrencyCode,
                EShopPaymentStatus = payment.Status.ToString()
            });
        }

        return new ReconciliationResult
        {
            From = from,
            To = to,
            Lines = lines,
            PayPalTransactionCount = report.Transactions.Count,
            MatchedCount = lines.Count(l => l.Match == ReconciliationMatch.Matched),
            PayPalOnlyCount = lines.Count(l => l.Match == ReconciliationMatch.PayPalOnly),
            EShopOnlyCount = lines.Count(l => l.Match == ReconciliationMatch.EShopOnly),
            PagesFetched = report.PagesFetched,
            Complete = report.Complete,
            TruncatedAtPage = report.TruncatedAtPage
        };
    }

    // ---------------------------------------------------------------- Helpers

    private async Task<Payment> LoadPaymentAsync(int orderId, CancellationToken cancellationToken)
    {
        return await _paymentRepository.FirstOrDefaultAsync(new PaymentByOrderIdSpecification(orderId), cancellationToken)
            ?? throw new PaymentNotFoundException($"No payment was found for order {orderId}.");
    }

    private async Task<Payment> LoadOwnedPaymentAsync(int orderId, string buyerId, CancellationToken cancellationToken)
    {
        var payment = await _paymentRepository.FirstOrDefaultAsync(new PaymentByOrderIdSpecification(orderId), cancellationToken);
        if (payment is null || payment.BuyerId != buyerId)
            throw new PaymentNotFoundException($"Order {orderId} was not found.");
        return payment;
    }

    private async Task SaveWithConcurrencyGuard(int orderId, CancellationToken cancellationToken)
    {
        try
        {
            await _paymentRepository.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            // Another request updated this payment concurrently. The provider call was idempotent
            // (same PayPalRequestId), so no double movement occurred; the caller can re-read the order.
            _logger.LogWarning("Concurrent update on order {OrderId}; the request was already applied by another call.", orderId);
            throw new PaymentConflictException($"Order {orderId} was updated concurrently; please re-read its state.");
        }
    }

    private async Task TrySaveAsync(CancellationToken cancellationToken)
    {
        try { await _paymentRepository.SaveChangesAsync(cancellationToken); }
        catch (DbUpdateConcurrencyException) { /* best-effort state update; ignore lost race */ }
    }

    /// <summary>Replay a write once under its (unchanged) idempotency key when the outcome is unknown.</summary>
    private async Task<T> WithUnknownReplay<T>(Func<Task<T>> operation, string label)
    {
        try
        {
            return await operation();
        }
        catch (PaymentGatewayException ex) when (ex.Kind == PaymentGatewayFailureKind.Unknown)
        {
            _logger.LogWarning(ex, "PayPal {Operation} outcome unknown; replaying under the same idempotency key.", label);
            return await operation();
        }
    }

    private PaymentView ToView(Payment payment) => new()
    {
        OrderId = payment.OrderId,
        Status = payment.Status.ToString(),
        CurrencyCode = payment.CurrencyCode,
        Amount = payment.Amount,
        PayPalOrderId = payment.PayPalOrderId,
        AuthorizationId = payment.AuthorizationId,
        AuthorizationStatus = payment.AuthorizationStatus,
        AuthorizationExpiresAt = payment.AuthorizationExpiresAt,
        CaptureId = payment.CaptureId,
        CapturedAmount = payment.CapturedAmount,
        PayPalFee = payment.PayPalFee,
        NetProceeds = payment.NetAmount,
        TotalRefunded = payment.TotalRefunded(),
        RefundableRemaining = payment.RefundableRemaining(),
        Refunds = payment.Refunds.Select(r => new RefundView
        {
            RefundId = r.PayPalRefundId,
            Amount = r.Amount,
            Status = r.Status,
            CreatedAt = r.CreatedAt
        }).ToList()
    };

    private static string? FirstNonEmpty(params string?[] values) =>
        values.FirstOrDefault(v => !string.IsNullOrWhiteSpace(v));

    private static string Truncate(string value, int max) =>
        value.Length <= max ? value : value[..max];
}
