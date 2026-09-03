using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.eShopWeb.ApplicationCore.Entities;
using Microsoft.eShopWeb.ApplicationCore.Entities.BuyerAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.Infrastructure.Data;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Money = PayPalServerSdk.Models.Money;
using Order = Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate.Order;
using Address = Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate.Address;
using PaymentMethod = Microsoft.eShopWeb.ApplicationCore.Entities.BuyerAggregate.PaymentMethod;
using TransactionDetails = PayPalServerSdk.Models.TransactionDetails;
using TransactionInformation = PayPalServerSdk.Models.TransactionInformation;

namespace Microsoft.eShopWeb.Infrastructure.Payments;

public sealed class PaymentWorkflowService : IPaymentWorkflowService
{
    private static readonly TimeSpan AuthorizationHonorPeriod = TimeSpan.FromDays(3);
    private static readonly TimeSpan ReportingLag = TimeSpan.FromHours(3);
    private readonly CatalogContext _db;
    private readonly PayPalGateway _paypal;
    private readonly PaymentOperationLock _locks;
    private readonly PayPalSettings _settings;
    private readonly ILogger<PaymentWorkflowService> _logger;

    public PaymentWorkflowService(CatalogContext db, PayPalGateway paypal, PaymentOperationLock locks,
        IOptions<PayPalSettings> settings, ILogger<PaymentWorkflowService> logger)
    {
        _db = db;
        _paypal = paypal;
        _locks = locks;
        _settings = settings.Value;
        _logger = logger;
    }

    public async Task<Order> PlaceOrderAsync(string buyerId, IReadOnlyList<OrderLineInput> items,
        Address shippingAddress, CancellationToken ct)
    {
        RequireBuyer(buyerId);
        if (items.Count == 0 || items.Any(x => x.CatalogItemId <= 0 || x.Quantity <= 0))
            throw BadRequest("INVALID_ORDER_ITEMS", "At least one catalog item with a positive quantity is required.");

        var quantities = items.GroupBy(x => x.CatalogItemId)
            .ToDictionary(x => x.Key, x => x.Sum(y => y.Quantity));
        var catalogItems = await _db.CatalogItems
            .Where(x => quantities.Keys.Contains(x.Id))
            .ToListAsync(ct);
        if (catalogItems.Count != quantities.Count)
            throw BadRequest("CATALOG_ITEM_NOT_FOUND", "One or more catalog items do not exist.");

        var orderItems = catalogItems.Select(item => new OrderItem(
            new CatalogItemOrdered(item.Id, item.Name, item.PictureUri),
            item.Price,
            quantities[item.Id])).ToList();
        var order = new Order(buyerId, shippingAddress, orderItems, _settings.Currency);
        _db.Orders.Add(order);
        await _db.SaveChangesAsync(ct);
        _logger.LogInformation("Order {OrderId} created awaiting payment for buyer {BuyerId}.", order.Id, buyerId);
        return order;
    }

    public async Task<Order> PayAsync(int orderId, string buyerId, CardInput? card,
        int? paymentMethodId, CancellationToken ct)
    {
        RequireBuyer(buyerId);
        if ((card is null) == (paymentMethodId is null))
            throw BadRequest("PAYMENT_SOURCE_REQUIRED", "Provide either card details or one saved paymentMethodId.");
        ValidateCard(card);

        using var held = await _locks.AcquireAsync($"order:{orderId}", ct);
        var order = await LoadOrderAsync(orderId, ct);
        EnsureOwner(order, buyerId);
        if (order.PaymentStatus is PaymentStatus.Authorized or PaymentStatus.CapturePending or
            PaymentStatus.Captured or PaymentStatus.PartiallyRefunded or PaymentStatus.Refunded)
            return order;
        if (order.PaymentStatus is PaymentStatus.Cancelled or PaymentStatus.CancellationPending)
            throw Conflict("ORDER_CANCELLED", "A cancelled order cannot be paid.");
        if (order.PaymentStatus == PaymentStatus.AuthorizationFailed)
            throw Conflict("PAYMENT_RETRY_REQUIRES_NEW_ORDER", "This payment attempt was rejected; place a new order to use a different payment source.");

        string? vaultId = null;
        if (paymentMethodId is not null)
        {
            var buyer = await LoadBuyerAsync(buyerId, ct);
            var method = buyer?.FindPaymentMethod(paymentMethodId.Value);
            if (method is null || method.Status != PaymentMethodStatus.Active || string.IsNullOrWhiteSpace(method.CardId))
                throw NotFound("PAYMENT_METHOD_NOT_FOUND", "The saved payment method was not found.");
            vaultId = method.CardId;
        }

        order.BeginAuthorization();
        await _db.SaveChangesAsync(ct);
        try
        {
            if (string.IsNullOrWhiteSpace(order.PayPalOrderId))
            {
                var providerOrder = await _paypal.CreateOrderAsync(order.Id, order.PaymentReference, order.Total(), order.Currency, card, vaultId, ct);
                order.RecordPayPalOrder(providerOrder.OrderId, providerOrder.Status);
                if (providerOrder.Authorization is { } embeddedAuthorization)
                {
                    ValidateAuthorization(order, embeddedAuthorization);
                    order.MarkAuthorized(embeddedAuthorization.AuthorizationId,
                        embeddedAuthorization.Status ?? "CREATED", embeddedAuthorization.CreatedAt,
                        embeddedAuthorization.ExpiresAt);
                }
                await _db.SaveChangesAsync(ct);
            }

            if (order.PaymentStatus == PaymentStatus.Authorized)
                return order;

            var authorization = await _paypal.AuthorizeOrderAsync(order.PaymentReference, order.PayPalOrderId!, ct);
            ValidateAuthorization(order, authorization);
            order.MarkAuthorized(authorization.AuthorizationId, authorization.Status ?? "CREATED",
                authorization.CreatedAt, authorization.ExpiresAt);
            await _db.SaveChangesAsync(ct);
            _logger.LogInformation("Order {OrderId} authorized as PayPal authorization {AuthorizationId}.",
                order.Id, authorization.AuthorizationId);
            return order;
        }
        catch (PaymentWorkflowException ex) when (ex.StatusCode is >= 400 and < 500)
        {
            order.MarkAuthorizationFailed();
            await _db.SaveChangesAsync(ct);
            throw;
        }
    }

    public async Task<Order> FulfilAsync(int orderId, CancellationToken ct)
    {
        using var held = await _locks.AcquireAsync($"order:{orderId}", ct);
        var order = await LoadOrderAsync(orderId, ct);
        if (order.PaymentStatus is PaymentStatus.Captured or PaymentStatus.PartiallyRefunded or PaymentStatus.Refunded)
            return order;
        if (order.PaymentStatus == PaymentStatus.CapturePending && !string.IsNullOrWhiteSpace(order.CaptureId))
        {
            var existingCapture = await _paypal.GetCaptureAsync(order.CaptureId, ct);
            if (existingCapture.Amount != order.Total())
                throw new PaymentWorkflowException(502, "PAYPAL_AMOUNT_MISMATCH",
                    "PayPal reported a capture amount that does not match the order total.");
            if (string.Equals(existingCapture.Status, "COMPLETED", StringComparison.OrdinalIgnoreCase))
            {
                order.MarkCaptured(existingCapture.CaptureId, "COMPLETED", existingCapture.Amount,
                    existingCapture.Fee, existingCapture.Net);
                await _db.SaveChangesAsync(ct);
                return order;
            }
            if (string.Equals(existingCapture.Status, "PENDING", StringComparison.OrdinalIgnoreCase))
                throw Conflict("CAPTURE_PENDING", "PayPal is still processing this capture; retry fulfilment later.");
            order.MarkCaptureFailed();
            await _db.SaveChangesAsync(ct);
            throw Conflict("CAPTURE_NOT_COMPLETED",
                $"PayPal capture status {existingCapture.Status ?? "UNKNOWN"} requires operator review.");
        }
        if (string.IsNullOrWhiteSpace(order.AuthorizationId))
            throw Conflict("ORDER_NOT_AUTHORIZED", "The order has no PayPal authorization to capture.");
        if (order.PaymentStatus is not (PaymentStatus.Authorized or PaymentStatus.CapturePending or PaymentStatus.CaptureFailed))
            throw Conflict("ORDER_NOT_CAPTURABLE", "The order is not in a capturable state.");

        var authorization = await _paypal.GetAuthorizationAsync(order.AuthorizationId, ct);
        if (string.Equals(authorization.Status, "VOIDED", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(authorization.Status, "DENIED", StringComparison.OrdinalIgnoreCase))
            throw Conflict("AUTHORIZATION_UNAVAILABLE", "The PayPal authorization is voided or denied; ask the shopper to place and pay a new order.");

        var now = DateTimeOffset.UtcNow;
        if (authorization.ExpiresAt is not null && authorization.ExpiresAt <= now)
            throw Conflict("AUTHORIZATION_CANNOT_BE_RENEWED", "The PayPal authorization is outside its renewal window; ask the shopper to place and pay a new order.");

        var createdAt = authorization.CreatedAt ?? order.AuthorizationCreatedAt;
        if (createdAt is null || now - createdAt >= AuthorizationHonorPeriod)
        {
            try
            {
                authorization = await _paypal.ReauthorizeAsync(order.PaymentReference, authorization.AuthorizationId,
                    order.Total(), order.Currency, ct);
                if (authorization.Amount != order.Total())
                    throw new PaymentWorkflowException(502, "PAYPAL_AMOUNT_MISMATCH",
                        "PayPal renewed an amount that does not match the order total.");
                order.MarkAuthorized(authorization.AuthorizationId, authorization.Status ?? "UNKNOWN",
                    authorization.CreatedAt, authorization.ExpiresAt);
                await _db.SaveChangesAsync(ct);
            }
            catch (PaymentWorkflowException ex) when (ex.StatusCode is >= 400 and < 500)
            {
                throw new PaymentWorkflowException(409, "AUTHORIZATION_CANNOT_BE_RENEWED",
                    "PayPal can no longer renew this authorization; ask the shopper to place and pay a new order.",
                    ex.ProviderDebugId, ex);
            }
        }

        if (!string.Equals(authorization.Status, "CREATED", StringComparison.OrdinalIgnoreCase))
            throw Conflict("AUTHORIZATION_NOT_CAPTURABLE",
                $"PayPal authorization status {authorization.Status ?? "UNKNOWN"} cannot be captured.");

        order.BeginCapture();
        await _db.SaveChangesAsync(ct);
        try
        {
            var capture = await _paypal.CaptureAsync(order.PaymentReference, authorization.AuthorizationId,
                order.Total(), order.Currency, ct);
            if (capture.Amount != order.Total())
                throw new PaymentWorkflowException(502, "PAYPAL_AMOUNT_MISMATCH",
                    "PayPal captured an amount that does not match the order total.");
            if (!string.Equals(capture.Status, "COMPLETED", StringComparison.OrdinalIgnoreCase) &&
                !string.Equals(capture.Status, "PENDING", StringComparison.OrdinalIgnoreCase))
                throw new PaymentWorkflowException(422, "CAPTURE_NOT_COMPLETED",
                    $"PayPal returned capture status {capture.Status ?? "UNKNOWN"}.");
            if (string.Equals(capture.Status, "PENDING", StringComparison.OrdinalIgnoreCase))
            {
                order.MarkCapturePending(capture.CaptureId, "PENDING", capture.Amount);
                await _db.SaveChangesAsync(ct);
                throw Conflict("CAPTURE_PENDING", "PayPal is still processing this capture; retry fulfilment later.");
            }
            order.MarkCaptured(capture.CaptureId, capture.Status ?? "UNKNOWN", capture.Amount, capture.Fee, capture.Net);
            await _db.SaveChangesAsync(ct);
            _logger.LogInformation("Order {OrderId} fulfilled with PayPal capture {CaptureId} status {Status}.",
                order.Id, capture.CaptureId, capture.Status);
            return order;
        }
        catch (PaymentWorkflowException ex) when (ex.StatusCode is >= 400 and < 500 && ex.Code != "CAPTURE_PENDING")
        {
            order.MarkCaptureFailed();
            await _db.SaveChangesAsync(ct);
            throw;
        }
    }

    public async Task<Order> CancelAsync(int orderId, CancellationToken ct)
    {
        using var held = await _locks.AcquireAsync($"order:{orderId}", ct);
        var order = await LoadOrderAsync(orderId, ct);
        if (order.PaymentStatus == PaymentStatus.Cancelled) return order;
        if (order.FulfilmentStatus == FulfilmentStatus.Fulfilled || !string.IsNullOrWhiteSpace(order.CaptureId))
            throw Conflict("ORDER_ALREADY_FULFILLED", "A fulfilled order must be refunded, not cancelled.");
        if (string.IsNullOrWhiteSpace(order.AuthorizationId))
            throw Conflict("ORDER_NOT_AUTHORIZED", "The order has no held funds to release.");

        order.BeginCancellation();
        await _db.SaveChangesAsync(ct);
        var status = await _paypal.VoidAsync(order.PaymentReference, order.AuthorizationId, ct);
        order.MarkCancelled(status);
        await _db.SaveChangesAsync(ct);
        _logger.LogInformation("Order {OrderId} cancelled and PayPal authorization {AuthorizationId} voided.",
            order.Id, order.AuthorizationId);
        return order;
    }

    public async Task<OrderRefund> RefundAsync(int orderId, string buyerId, decimal? amount,
        string idempotencyKey, CancellationToken ct)
    {
        RequireBuyer(buyerId);
        if (string.IsNullOrWhiteSpace(idempotencyKey) || idempotencyKey.Length > 108)
            throw BadRequest("INVALID_IDEMPOTENCY_KEY", "idempotencyKey is required and cannot exceed 108 characters.");

        using var held = await _locks.AcquireAsync($"order:{orderId}", ct);
        var order = await LoadOrderAsync(orderId, ct);
        EnsureOwner(order, buyerId);
        if (string.IsNullOrWhiteSpace(order.CaptureId) || order.CapturedAmount is null)
            throw Conflict("ORDER_NOT_CAPTURED", "Only a captured order can be refunded.");

        var existing = order.Refunds.SingleOrDefault(x => x.IdempotencyKey == idempotencyKey);
        if (existing is not null && amount is not null && existing.Amount != amount.Value)
            throw Conflict("IDEMPOTENCY_KEY_REUSED",
                "This idempotencyKey was already used for a different refund amount.");
        var refundAmount = existing?.Amount ?? amount ?? (order.CapturedAmount.Value - order.RefundedAmount);
        if (refundAmount <= 0 || decimal.Round(refundAmount, 2) != refundAmount)
            throw BadRequest("INVALID_REFUND_AMOUNT", "Refund amount must be positive and have no more than two decimal places.");
        if (existing is null && refundAmount > order.CapturedAmount.Value - order.RefundedAmount)
            throw Conflict("REFUND_EXCEEDS_CAPTURE",
                "The refund exceeds the captured amount remaining.");
        var refund = order.ReserveRefund(idempotencyKey, refundAmount);
        if (refund.Status == PaymentOperationStatus.Completed) return refund;
        try
        {
            await _db.SaveChangesAsync(ct);
        }
        catch (DbUpdateConcurrencyException ex)
        {
            throw new PaymentWorkflowException(409, "PAYMENT_CONCURRENCY_CONFLICT",
                "Another refund changed this order; retry the request with the same idempotencyKey.",
                innerException: ex);
        }
        catch (DbUpdateException ex)
        {
            throw new PaymentWorkflowException(409, "REFUND_IDEMPOTENCY_CONFLICT",
                "This refund is already being processed; retry with the same idempotencyKey.",
                innerException: ex);
        }

        try
        {
            var providerRefund = string.IsNullOrWhiteSpace(refund.PayPalRefundId)
                ? await _paypal.RefundAsync(order.CaptureId, refundAmount,
                    order.Currency, idempotencyKey, order.PaymentReference, ct)
                : await _paypal.GetRefundAsync(refund.PayPalRefundId, ct);
            if (providerRefund.Amount != refundAmount)
                throw new PaymentWorkflowException(502, "PAYPAL_AMOUNT_MISMATCH",
                    "PayPal refunded an amount that does not match the requested amount.");
            if (string.Equals(providerRefund.Status, "COMPLETED", StringComparison.OrdinalIgnoreCase))
            {
                refund.Complete(providerRefund.RefundId, providerRefund.Status);
                order.RefreshRefundState();
            }
            else if (string.Equals(providerRefund.Status, "PENDING", StringComparison.OrdinalIgnoreCase))
            {
                refund.MarkPending(providerRefund.RefundId, providerRefund.Status);
            }
            else
            {
                refund.Fail(providerRefund.Status);
                await _db.SaveChangesAsync(ct);
                throw new PaymentWorkflowException(422, "REFUND_NOT_COMPLETED",
                    $"PayPal refund status {providerRefund.Status} requires review.");
            }
            await _db.SaveChangesAsync(ct);
            _logger.LogInformation("Order {OrderId} refund {RefundId} recorded with PayPal status {Status}.",
                order.Id, providerRefund.RefundId, providerRefund.Status);
            return refund;
        }
        catch (PaymentWorkflowException ex)
        {
            if (ex.StatusCode is >= 400 and < 500) refund.Fail(ex.Code);
            else refund.MarkUnknown();
            await _db.SaveChangesAsync(ct);
            throw;
        }
    }

    public async Task<IReadOnlyList<Order>> GetOrdersAsync(string buyerId, CancellationToken ct)
    {
        RequireBuyer(buyerId);
        return await _db.Orders.AsNoTracking()
            .Where(x => x.BuyerId == buyerId)
            .Include(x => x.OrderItems)
            .Include(x => x.Refunds)
            .OrderByDescending(x => x.OrderDate)
            .ToListAsync(ct);
    }

    public async Task<PaymentMethod> SavePaymentMethodAsync(string buyerId, string alias,
        CardInput card, CancellationToken ct)
    {
        RequireBuyer(buyerId);
        ValidateCard(card);
        if (string.IsNullOrWhiteSpace(alias) || alias.Length > 64)
            throw BadRequest("INVALID_ALIAS", "alias is required and cannot exceed 64 characters.");

        using var held = await _locks.AcquireAsync($"buyer:{buyerId}", ct);
        var buyer = await LoadBuyerAsync(buyerId, ct);
        if (buyer is null)
        {
            buyer = new Buyer(buyerId);
            _db.Buyers.Add(buyer);
        }
        var operationKey = $"eshop-vault-{Guid.NewGuid():N}";
        var method = buyer.AddPaymentMethod(alias.Trim(), operationKey);
        await _db.SaveChangesAsync(ct);

        var saved = await _paypal.SaveCardAsync(buyerId, operationKey, card, ct);
        method.Activate(saved.VaultId, saved.Last4, saved.Brand, saved.Expiry, saved.CustomerId);
        await _db.SaveChangesAsync(ct);
        _logger.LogInformation("Payment method {PaymentMethodId} saved for buyer {BuyerId} as PayPal token {VaultId}.",
            method.Id, buyerId, saved.VaultId);
        return method;
    }

    public async Task<IReadOnlyList<PaymentMethod>> GetPaymentMethodsAsync(string buyerId, CancellationToken ct)
    {
        RequireBuyer(buyerId);
        var buyer = await _db.Buyers.AsNoTracking()
            .Include(x => x.PaymentMethods)
            .SingleOrDefaultAsync(x => x.IdentityGuid == buyerId, ct);
        return buyer is null
            ? Array.Empty<PaymentMethod>()
            : buyer.PaymentMethods.Where(x => x.Status == PaymentMethodStatus.Active).ToList();
    }

    public async Task DeletePaymentMethodAsync(int paymentMethodId, string buyerId, CancellationToken ct)
    {
        RequireBuyer(buyerId);
        using var held = await _locks.AcquireAsync($"buyer:{buyerId}", ct);
        var buyer = await LoadBuyerAsync(buyerId, ct);
        var method = buyer?.FindPaymentMethod(paymentMethodId);
        if (buyer is null || method is null || string.IsNullOrWhiteSpace(method.CardId))
            throw NotFound("PAYMENT_METHOD_NOT_FOUND", "The saved payment method was not found.");

        method.BeginDelete();
        await _db.SaveChangesAsync(ct);
        try
        {
            await _paypal.DeleteCardAsync(method.CardId, ct);
        }
        catch (PaymentWorkflowException ex) when (ex.StatusCode == 404)
        {
            _logger.LogInformation("PayPal token for payment method {PaymentMethodId} was already absent.", method.Id);
        }
        buyer.RemovePaymentMethod(method);
        await _db.SaveChangesAsync(ct);
    }

    public async Task<ReconciliationReport> ReconcileAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken ct)
    {
        if (from >= to) throw BadRequest("INVALID_DATE_RANGE", "from must be earlier than to.");
        if (to - from > TimeSpan.FromDays(366 * 3))
            throw BadRequest("DATE_RANGE_TOO_LARGE", "PayPal transaction search supports the previous three years.");

        var orders = await _db.Orders.AsNoTracking()
            .Include(x => x.Refunds)
            .Where(x => x.OrderDate <= to &&
                (x.OrderDate >= from || x.FulfilledAt >= from || x.Refunds.Any(r => r.CreatedAt >= from)))
            .ToListAsync(ct);

        var provider = new Dictionary<string, TransactionInformation>(StringComparer.Ordinal);
        // PayPal reporting is not a live ledger. Do not query its known lag window: local
        // transactions in that slice are reported below as pending provider reporting.
        var reportingCutoff = DateTimeOffset.UtcNow - ReportingLag;
        var providerTo = to < reportingCutoff ? to : reportingCutoff;
        var windowStart = from;
        var totalPageCount = 0;
        while (windowStart < providerTo)
        {
            var windowEnd = windowStart.AddDays(31);
            if (windowEnd > providerTo) windowEnd = providerTo;
            var page = 1;
            while (true)
            {
                if (++totalPageCount > 10_000)
                    throw new PaymentWorkflowException(502, "PAYPAL_REPORT_LIMIT",
                        "PayPal reporting exceeded the safety page limit; narrow the date range.");
                var result = await _paypal.SearchTransactionsAsync(windowStart, windowEnd, page, ct);
                foreach (var item in result.TransactionDetails ?? (IReadOnlyList<TransactionDetails>)Array.Empty<TransactionDetails>())
                {
                    var transaction = item.TransactionInfo;
                    if (transaction is null) continue;
                    var key = string.Join('|', transaction.TransactionId, transaction.TransactionEventCode,
                        transaction.TransactionInitiationDate, transaction.TransactionAmount?.Value);
                    provider[key] = transaction;
                }
                var count = result.TransactionDetails?.Count ?? 0;
                if (result.TotalPages is int totalPages ? page >= totalPages : count < 100) break;
                page++;
            }
            windowStart = windowEnd;
        }

        var byProviderId = new Dictionary<string, Order>(StringComparer.Ordinal);
        var byInvoice = orders.ToDictionary(x => $"ESHOP-{x.PaymentReference:N}", x => x, StringComparer.OrdinalIgnoreCase);
        foreach (var order in orders)
        {
            Add(byProviderId, order.PayPalOrderId, order);
            Add(byProviderId, order.AuthorizationId, order);
            Add(byProviderId, order.CaptureId, order);
            foreach (var refund in order.Refunds) Add(byProviderId, refund.PayPalRefundId, order);
        }

        var entries = new List<ReconciliationEntry>();
        var seenIds = new HashSet<string>(StringComparer.Ordinal);
        foreach (var transaction in provider.Values)
        {
            Order? order = null;
            if (!string.IsNullOrWhiteSpace(transaction.TransactionId))
                byProviderId.TryGetValue(transaction.TransactionId, out order);
            if (order is null && !string.IsNullOrWhiteSpace(transaction.PaypalReferenceId))
                byProviderId.TryGetValue(transaction.PaypalReferenceId, out order);
            if (order is null && !string.IsNullOrWhiteSpace(transaction.InvoiceId))
                byInvoice.TryGetValue(transaction.InvoiceId, out order);
            if (!string.IsNullOrWhiteSpace(transaction.TransactionId)) seenIds.Add(transaction.TransactionId);
            entries.Add(new ReconciliationEntry(transaction.TransactionId, order?.Id, transaction.InvoiceId,
                transaction.TransactionEventCode, transaction.TransactionStatus,
                ParseMoney(transaction.TransactionAmount), ParseMoney(transaction.FeeAmount),
                transaction.TransactionAmount?.CurrencyCode, ParseDate(transaction.TransactionInitiationDate),
                order is null ? "MissingInEShop" : "Matched"));
        }

        var lagCutoff = reportingCutoff;
        foreach (var order in orders)
        {
            AddMissing(entries, seenIds, order, order.AuthorizationId, order.AuthorizationStatus,
                order.Total(), order.AuthorizationCreatedAt ?? order.OrderDate, from, to, lagCutoff);
            AddMissing(entries, seenIds, order, order.CaptureId, order.CaptureStatus,
                order.CapturedAmount, order.FulfilledAt, from, to, lagCutoff);
            foreach (var refund in order.Refunds.Where(x => x.Status == PaymentOperationStatus.Completed))
                AddMissing(entries, seenIds, order, refund.PayPalRefundId, refund.PayPalStatus,
                    -refund.Amount, refund.CompletedAt, from, to, lagCutoff);
        }

        return new ReconciliationReport(from, to,
            entries.OrderBy(x => x.OccurredAt).ThenBy(x => x.PayPalTransactionId).ToList(),
            to >= lagCutoff);
    }

    private async Task<Order> LoadOrderAsync(int orderId, CancellationToken ct) =>
        await _db.Orders.Include(x => x.OrderItems).Include(x => x.Refunds)
            .SingleOrDefaultAsync(x => x.Id == orderId, ct)
        ?? throw NotFound("ORDER_NOT_FOUND", "The order was not found.");

    private async Task<Buyer?> LoadBuyerAsync(string buyerId, CancellationToken ct) =>
        await _db.Buyers.Include(x => x.PaymentMethods)
            .SingleOrDefaultAsync(x => x.IdentityGuid == buyerId, ct);

    private static void ValidateCard(CardInput? card)
    {
        if (card is null) return;
        var number = new string(card.Number.Where(char.IsDigit).ToArray());
        if (string.IsNullOrWhiteSpace(card.Name) || number.Length is < 13 or > 19 ||
            string.IsNullOrWhiteSpace(card.SecurityCode) || card.SecurityCode.Length is < 3 or > 4 ||
            !DateTime.TryParseExact(card.Expiry, "yyyy-MM", CultureInfo.InvariantCulture,
                DateTimeStyles.None, out var expiry) || expiry < new DateTime(DateTime.UtcNow.Year, DateTime.UtcNow.Month, 1) ||
            string.IsNullOrWhiteSpace(card.BillingAddress.CountryCode) || card.BillingAddress.CountryCode.Length != 2)
            throw BadRequest("INVALID_CARD", "Card details are incomplete or invalid.");
    }

    private static void EnsureOwner(Order order, string buyerId)
    {
        if (!string.Equals(order.BuyerId, buyerId, StringComparison.Ordinal))
            throw NotFound("ORDER_NOT_FOUND", "The order was not found.");
    }

    private static void ValidateAuthorization(Order order, PayPalAuthorizationResult authorization)
    {
        if (authorization.Amount != order.Total())
            throw new PaymentWorkflowException(502, "PAYPAL_AMOUNT_MISMATCH",
                "PayPal authorized an amount that does not match the order total.");
        if (!string.Equals(authorization.Status, "CREATED", StringComparison.OrdinalIgnoreCase))
            throw new PaymentWorkflowException(422, "AUTHORIZATION_NOT_CREATED",
                $"PayPal returned authorization status {authorization.Status ?? "UNKNOWN"}.");
    }

    private static void RequireBuyer(string buyerId)
    {
        if (string.IsNullOrWhiteSpace(buyerId))
            throw new PaymentWorkflowException(401, "AUTHENTICATION_REQUIRED", "A signed-in shopper is required.");
    }

    private static void Add(Dictionary<string, Order> target, string? id, Order order)
    { if (!string.IsNullOrWhiteSpace(id)) target[id] = order; }

    private static void AddMissing(List<ReconciliationEntry> entries, HashSet<string> seen, Order order,
        string? providerId, string? status, decimal? amount, DateTimeOffset? occurredAt,
        DateTimeOffset from, DateTimeOffset to, DateTimeOffset lagCutoff)
    {
        if (string.IsNullOrWhiteSpace(providerId) || seen.Contains(providerId) || occurredAt is null ||
            occurredAt < from || occurredAt > to) return;
        entries.Add(new ReconciliationEntry(providerId, order.Id, $"ESHOP-{order.Id}", null, status,
            amount, null, order.Currency, occurredAt,
            occurredAt >= lagCutoff ? "PendingProviderReporting" : "MissingInPayPal"));
    }

    private static decimal? ParseMoney(Money? value) => value is null ||
        !decimal.TryParse(value.Value, NumberStyles.Number, CultureInfo.InvariantCulture, out var parsed)
            ? null : parsed;

    private static DateTimeOffset? ParseDate(string? value) =>
        DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var parsed)
            ? parsed : null;

    private static PaymentWorkflowException BadRequest(string code, string message) => new(400, code, message);
    private static PaymentWorkflowException NotFound(string code, string message) => new(404, code, message);
    private static PaymentWorkflowException Conflict(string code, string message) => new(409, code, message);
}
