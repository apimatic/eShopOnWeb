using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.eShopWeb.ApplicationCore.Entities;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.Infrastructure.Data;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.PublicApi.Payments;

public sealed class PaymentApplicationService
{
    private readonly CatalogContext _db;
    private readonly IPayPalGateway _payPal;
    private readonly PaymentOperationLock _operationLock;
    private readonly PayPalOptions _options;

    public PaymentApplicationService(CatalogContext db, IPayPalGateway payPal,
        PaymentOperationLock operationLock, IOptions<PayPalOptions> options)
    {
        _db = db;
        _payPal = payPal;
        _operationLock = operationLock;
        _options = options.Value;
    }

    public async Task<Order> CreateOrderAsync(string buyerId, CreateOrderRequest request,
        CancellationToken cancellationToken)
    {
        var requestedItems = request.Items
            .GroupBy(x => x.CatalogItemId)
            .Select(x => new { CatalogItemId = x.Key, Quantity = x.Sum(y => y.Quantity) })
            .ToList();
        if (requestedItems.Any(x => x.Quantity is <= 0 or > 1000))
        {
            throw new PaymentOperationException(400, "invalid_quantity",
                "Each catalog item quantity must total between 1 and 1000.");
        }

        var ids = requestedItems.Select(x => x.CatalogItemId).ToList();
        var catalogItems = await _db.CatalogItems
            .Where(x => ids.Contains(x.Id))
            .ToDictionaryAsync(x => x.Id, cancellationToken);
        var missingIds = ids.Where(x => !catalogItems.ContainsKey(x)).ToList();
        if (missingIds.Count > 0)
        {
            throw new PaymentOperationException(400, "catalog_items_not_found",
                "Unknown catalog item ids: " + string.Join(", ", missingIds));
        }

        var items = requestedItems.Select(requested =>
        {
            var catalogItem = catalogItems[requested.CatalogItemId];
            return new OrderItem(
                new CatalogItemOrdered(catalogItem.Id, catalogItem.Name, catalogItem.PictureUri),
                catalogItem.Price, requested.Quantity);
        }).ToList();
        var address = request.ShippingAddress;
        var order = new Order(buyerId,
            new Address(address.Street, address.City, address.State, address.Country, address.ZipCode),
            items, _options.Currency);
        _db.Orders.Add(order);
        await _db.SaveChangesAsync(cancellationToken);
        return order;
    }

    public async Task<Order> PayAsync(int orderId, string buyerId, PayOrderRequest request,
        CancellationToken cancellationToken)
    {
        using var heldLock = await _operationLock.AcquireAsync($"order:{orderId}", cancellationToken);
        var order = await ShopperOrderAsync(orderId, buyerId, cancellationToken);
        if (order.PaymentStatus == OrderPaymentStatus.Authorized)
        {
            return order;
        }
        if (order.PaymentStatus == OrderPaymentStatus.AuthorizationPending && order.AuthorizationId != null)
        {
            var current = await _payPal.GetAuthorizationAsync(order.AuthorizationId, cancellationToken);
            EnsureMoney(current.Amount, current.Currency, order);
            if (current.Status == "CREATED")
            {
                order.RecordAuthorization(current.Id, current.Status, current.CreatedAt,
                    current.ExpiresAt, current.Amount);
                await _db.SaveChangesAsync(cancellationToken);
            }
            else if (current.Status != "PENDING")
            {
                throw new PaymentOperationException(409, "authorization_not_usable",
                    $"PayPal authorization is {current.Status}; choose another card or payment method.");
            }
            return order;
        }
        if (order.PaymentStatus != OrderPaymentStatus.AwaitingPayment &&
            !(order.PaymentStatus == OrderPaymentStatus.AuthorizationPending && order.PayPalOrderId != null))
        {
            throw InvalidState(order, "pay");
        }

        SavedPaymentMethod? savedMethod = null;
        if (request.PaymentMethodId is { } paymentMethodId)
        {
            savedMethod = await _db.SavedPaymentMethods.SingleOrDefaultAsync(x =>
                x.Id == paymentMethodId && x.BuyerId == buyerId && x.DeletedAt == null,
                cancellationToken);
            if (savedMethod == null)
            {
                throw new PaymentOperationException(404, "payment_method_not_found",
                    "The saved payment method does not exist or is not available to this shopper.");
            }
        }

        if (order.PayPalOrderId == null)
        {
            var created = await _payPal.CreateOrderAsync(order.Id, order.PaymentReference!, order.Total(), order.Currency,
                $"eshop-order-{order.PaymentReference}-create", cancellationToken);
            order.StartPayment(created.Id, created.Status, savedMethod?.Id);
            await _db.SaveChangesAsync(cancellationToken);
        }

        // Raw card data is forwarded directly and is never attached to an entity or log scope.
        var authorization = await _payPal.AuthorizeOrderAsync(order.PayPalOrderId!,
            request.Card?.ToGatewayModel(), savedMethod?.PayPalPaymentTokenId,
            AuthorizeRequestId(order.PaymentReference!, request, savedMethod), cancellationToken);
        EnsureMoney(authorization.Amount, authorization.Currency, order);
        if (authorization.Status is not ("CREATED" or "PENDING"))
        {
            throw new PaymentOperationException(422, "authorization_declined",
                $"PayPal returned authorization status {authorization.Status}; use another payment method.");
        }
        order.RecordAuthorization(authorization.Id, authorization.Status,
            authorization.CreatedAt, authorization.ExpiresAt, authorization.Amount);
        await _db.SaveChangesAsync(cancellationToken);
        return order;
    }

    public async Task<Order> FulfilAsync(int orderId, CancellationToken cancellationToken)
    {
        using var heldLock = await _operationLock.AcquireAsync($"order:{orderId}", cancellationToken);
        var order = await OperatorOrderAsync(orderId, cancellationToken);
        if (order.PaymentStatus is OrderPaymentStatus.Fulfilled or
            OrderPaymentStatus.PartiallyRefunded or OrderPaymentStatus.Refunded)
        {
            return order;
        }
        if (order.PaymentStatus == OrderPaymentStatus.CapturePending && order.CaptureId != null)
        {
            var currentCapture = await _payPal.GetCaptureAsync(order.CaptureId, cancellationToken);
            ApplyCapture(order, currentCapture);
            await _db.SaveChangesAsync(cancellationToken);
            return order;
        }
        if (order.PaymentStatus != OrderPaymentStatus.Authorized || order.AuthorizationId == null ||
            order.AuthorizationCreatedAt == null || order.AuthorizationExpiresAt == null)
        {
            throw InvalidState(order, "fulfil");
        }

        if (DateTimeOffset.UtcNow >= order.AuthorizationCreatedAt.Value.AddDays(3))
        {
            if (DateTimeOffset.UtcNow >= order.AuthorizationExpiresAt.Value)
            {
                throw new PaymentOperationException(409, "authorization_expired",
                    "The PayPal authorization is outside its renewal window.",
                    "Cancel the order and ask the shopper to place and pay a new order.");
            }
            try
            {
                var renewed = await _payPal.ReauthorizeAsync(order.AuthorizationId, order.Total(),
                    order.Currency, $"eshop-order-{order.Id}-reauthorize-{order.AuthorizationRevision + 1}",
                    cancellationToken);
                EnsureMoney(renewed.Amount, renewed.Currency, order);
                order.RecordReauthorization(renewed.Id, renewed.Status, renewed.CreatedAt,
                    renewed.ExpiresAt, renewed.Amount);
                await _db.SaveChangesAsync(cancellationToken);
                if (renewed.Status != "CREATED") return order;
            }
            catch (PayPalApiException ex) when (ex.StatusCode is 404 or 409 or 422)
            {
                throw new PaymentOperationException(409, "authorization_cannot_be_renewed",
                    "PayPal can no longer renew this authorization: " + ex.Message,
                    "Cancel the order and ask the shopper to place and pay a new order.");
            }
        }

        var capture = await _payPal.CaptureAsync(order.AuthorizationId!, order.PaymentReference!, order.Total(),
            order.Currency, $"eshop-order-{order.PaymentReference}-capture", cancellationToken);
        ApplyCapture(order, capture);
        await _db.SaveChangesAsync(cancellationToken);
        return order;
    }

    public async Task<Order> CancelAsync(int orderId, CancellationToken cancellationToken)
    {
        using var heldLock = await _operationLock.AcquireAsync($"order:{orderId}", cancellationToken);
        var order = await OperatorOrderAsync(orderId, cancellationToken);
        if (order.PaymentStatus == OrderPaymentStatus.Cancelled) return order;
        if (order.PaymentStatus is OrderPaymentStatus.Fulfilled or OrderPaymentStatus.CapturePending or
            OrderPaymentStatus.PartiallyRefunded or OrderPaymentStatus.Refunded)
        {
            throw InvalidState(order, "cancel");
        }
        if (order.AuthorizationId != null)
        {
            await _payPal.VoidAsync(order.AuthorizationId, $"eshop-order-{order.Id}-void",
                cancellationToken);
        }
        order.Cancel(DateTimeOffset.UtcNow);
        await _db.SaveChangesAsync(cancellationToken);
        return order;
    }

    public async Task<(Order Order, PaymentRefund Refund)> RefundAsync(int orderId, string buyerId,
        RefundOrderRequest request, CancellationToken cancellationToken)
    {
        using var heldLock = await _operationLock.AcquireAsync($"order:{orderId}", cancellationToken);
        var order = await ShopperOrderAsync(orderId, buyerId, cancellationToken);
        var existing = order.Refunds.SingleOrDefault(x => x.IdempotencyKey == request.IdempotencyKey);
        if (existing != null) return (order, existing);
        if (order.PaymentStatus is not (OrderPaymentStatus.Fulfilled or
            OrderPaymentStatus.PartiallyRefunded) || order.CaptureId == null ||
            order.CapturedAmount == null)
        {
            throw InvalidState(order, "refund");
        }

        var remaining = order.CapturedAmount.Value - order.RefundedAmount;
        var amount = request.Amount ?? remaining;
        if (amount <= 0 || amount > remaining)
        {
            throw new PaymentOperationException(409, "refund_exceeds_remaining_capture",
                $"Refund must be positive and no more than {remaining:0.00} {order.Currency}.");
        }
        var providerRequestId = StableRequestId($"refund:{order.Id}:{request.IdempotencyKey}");
        var result = await _payPal.RefundAsync(order.CaptureId, amount, order.Currency,
            providerRequestId, cancellationToken);
        EnsureMoney(result.Amount, result.Currency, order, amount);
        var refund = order.AddRefund(result.Id, request.IdempotencyKey, result.Status,
            result.Amount, result.CreatedAt);
        await _db.SaveChangesAsync(cancellationToken);
        return (order, refund);
    }

    public async Task<IReadOnlyList<Order>> MyOrdersAsync(string buyerId,
        CancellationToken cancellationToken) =>
        await _db.Orders.AsNoTracking()
            .Where(x => x.BuyerId == buyerId)
            .Include(x => x.OrderItems)
            .Include(x => x.Refunds)
            .OrderByDescending(x => x.OrderDate)
            .ToListAsync(cancellationToken);

    public async Task<SavedPaymentMethod> SavePaymentMethodAsync(string buyerId,
        PaymentCard card, string? idempotencyKey, CancellationToken cancellationToken)
    {
        using var heldLock = await _operationLock.AcquireAsync($"vault:{buyerId}", cancellationToken);
        var customerId = await _db.SavedPaymentMethods
            .Where(x => x.BuyerId == buyerId && x.PayPalCustomerId != null)
            .Select(x => x.PayPalCustomerId)
            .FirstOrDefaultAsync(cancellationToken);
        var requestId = string.IsNullOrWhiteSpace(idempotencyKey)
            ? $"eshop-vault-{Guid.NewGuid():N}"
            : StableRequestId($"vault:{buyerId}:{idempotencyKey}");
        var saved = await _payPal.SaveCardAsync(buyerId, customerId, card, requestId,
            cancellationToken);
        var existing = await _db.SavedPaymentMethods.SingleOrDefaultAsync(x =>
            x.PayPalPaymentTokenId == saved.PaymentTokenId, cancellationToken);
        if (existing != null)
        {
            if (existing.BuyerId != buyerId || existing.IsDeleted)
            {
                throw new PaymentOperationException(409, "vault_idempotency_key_reused",
                    "This Idempotency-Key cannot be reused; save the card again with a new key.");
            }
            return existing;
        }
        var method = new SavedPaymentMethod(buyerId, saved.PaymentTokenId, saved.CustomerId,
            saved.Brand, saved.LastDigits, saved.Expiry, saved.CardholderName);
        _db.SavedPaymentMethods.Add(method);
        await _db.SaveChangesAsync(cancellationToken);
        return method;
    }

    public async Task<IReadOnlyList<SavedPaymentMethod>> PaymentMethodsAsync(string buyerId,
        CancellationToken cancellationToken) =>
        await _db.SavedPaymentMethods.AsNoTracking()
            .Where(x => x.BuyerId == buyerId && x.DeletedAt == null)
            .OrderByDescending(x => x.CreatedAt)
            .ToListAsync(cancellationToken);

    public async Task DeletePaymentMethodAsync(int id, string buyerId,
        CancellationToken cancellationToken)
    {
        using var heldLock = await _operationLock.AcquireAsync($"method:{id}", cancellationToken);
        var method = await _db.SavedPaymentMethods.SingleOrDefaultAsync(x =>
            x.Id == id && x.BuyerId == buyerId && x.DeletedAt == null, cancellationToken);
        if (method == null)
        {
            throw new PaymentOperationException(404, "payment_method_not_found",
                "The saved payment method does not exist or is not available to this shopper.");
        }
        await _payPal.DeletePaymentTokenAsync(method.PayPalPaymentTokenId, cancellationToken);
        method.Delete();
        await _db.SaveChangesAsync(cancellationToken);
    }

    public async Task<ReconciliationResponse> ReconcileAsync(DateTimeOffset from, DateTimeOffset to,
        CancellationToken cancellationToken)
    {
        if (to <= from)
        {
            throw new PaymentOperationException(400, "invalid_date_range", "to must be later than from.");
        }
        if (to > DateTimeOffset.UtcNow.AddMinutes(5))
        {
            throw new PaymentOperationException(400, "invalid_date_range", "to cannot be in the future.");
        }

        // Transaction Search can lag live activity by up to three hours. Query only its documented
        // availability window while still reporting all local records through the requested `to`.
        var reportingCutoff = DateTimeOffset.UtcNow.AddHours(-3);
        var payPalDataThrough = to < reportingCutoff ? to : reportingCutoff;
        IReadOnlyList<PayPalTransaction> payPalRecords = payPalDataThrough > from
            ? await _payPal.SearchTransactionsAsync(from, payPalDataThrough, cancellationToken)
            : Array.Empty<PayPalTransaction>();
        var orders = await _db.Orders.AsNoTracking()
            .Include(x => x.Refunds)
            .Where(x => (x.AuthorizationCreatedAt >= from && x.AuthorizationCreatedAt <= to) ||
                        (x.CapturedAt >= from && x.CapturedAt <= to) ||
                        x.Refunds.Any(r => r.CreatedAt >= from && r.CreatedAt <= to))
            .ToListAsync(cancellationToken);

        var idsToOrder = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        foreach (var order in orders)
        {
            AddId(idsToOrder, order.PayPalOrderId, order.Id);
            AddId(idsToOrder, order.PaymentReference, order.Id);
            AddId(idsToOrder, order.AuthorizationId, order.Id);
            AddId(idsToOrder, order.CaptureId, order.Id);
            foreach (var refund in order.Refunds) AddId(idsToOrder, refund.PayPalRefundId, order.Id);
        }
        int? Match(PayPalTransaction transaction)
        {
            if (idsToOrder.TryGetValue(transaction.TransactionId, out var byTransaction)) return byTransaction;
            if (transaction.ReferenceId != null &&
                idsToOrder.TryGetValue(transaction.ReferenceId, out var byReference)) return byReference;
            if (transaction.InvoiceId?.StartsWith("eshop-", StringComparison.OrdinalIgnoreCase) == true &&
                idsToOrder.TryGetValue(transaction.InvoiceId[6..], out var byInvoice)) return byInvoice;
            return null;
        }
        var payPalIds = payPalRecords
            .SelectMany(x => new[] { x.TransactionId, x.ReferenceId })
            .OfType<string>()
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var payPalOutput = payPalRecords.Select(x =>
        {
            var orderId = Match(x);
            return new ReconciliationPayPalRecord(x.TransactionId, x.ReferenceId, x.EventCode,
                x.Status, x.Amount, x.Currency, x.Fee, x.InitiatedAt, orderId, orderId != null);
        }).ToList();

        var local = new List<ReconciliationEShopRecord>();
        foreach (var order in orders)
        {
            if (order.AuthorizationId != null && order.AuthorizationCreatedAt >= from &&
                order.AuthorizationCreatedAt <= to)
            {
                local.Add(LocalRecord(order, "Authorization", order.AuthorizationId,
                    order.AuthorizationStatus ?? "UNKNOWN", order.Total(),
                    order.AuthorizationCreatedAt.Value, payPalIds));
            }
            if (order.CaptureId != null && order.CapturedAt >= from && order.CapturedAt <= to)
            {
                local.Add(LocalRecord(order, "Capture", order.CaptureId,
                    order.CaptureStatus ?? "UNKNOWN", order.CapturedAmount ?? 0,
                    order.CapturedAt.Value, payPalIds));
            }
            foreach (var refund in order.Refunds.Where(x => x.CreatedAt >= from && x.CreatedAt <= to))
            {
                local.Add(LocalRecord(order, "Refund", refund.PayPalRefundId, refund.Status,
                    refund.Amount, refund.CreatedAt, payPalIds));
            }
        }
        return new ReconciliationResponse(from, to, payPalDataThrough, payPalOutput, local);
    }

    private async Task<Order> ShopperOrderAsync(int id, string buyerId,
        CancellationToken cancellationToken)
    {
        var order = await _db.Orders.Include(x => x.OrderItems).Include(x => x.Refunds)
            .SingleOrDefaultAsync(x => x.Id == id && x.BuyerId == buyerId, cancellationToken);
        return order ?? throw new PaymentOperationException(404, "order_not_found",
            "The order does not exist or is not available to this shopper.");
    }

    private async Task<Order> OperatorOrderAsync(int id, CancellationToken cancellationToken)
    {
        var order = await _db.Orders.Include(x => x.OrderItems).Include(x => x.Refunds)
            .SingleOrDefaultAsync(x => x.Id == id, cancellationToken);
        return order ?? throw new PaymentOperationException(404, "order_not_found", "Order not found.");
    }

    private static void ApplyCapture(Order order, PayPalCaptureResult capture)
    {
        EnsureMoney(capture.Amount, capture.Currency, order);
        if (capture.Status == "COMPLETED")
        {
            order.RecordCapture(capture.Id, capture.Status, capture.Amount, capture.Fee,
                capture.Net, capture.CreatedAt);
        }
        else if (capture.Status == "PENDING")
        {
            order.RecordCapturePending(capture.Id, capture.Status);
        }
        else
        {
            throw new PaymentOperationException(409, "capture_not_completed",
                $"PayPal capture is {capture.Status}; review it in PayPal before retrying fulfilment.");
        }
    }

    private static void EnsureMoney(decimal amount, string currency, Order order,
        decimal? expectedAmount = null)
    {
        var expected = expectedAmount ?? order.Total();
        if (amount != expected || !currency.Equals(order.Currency, StringComparison.OrdinalIgnoreCase))
        {
            throw new PaymentOperationException(502, "paypal_amount_mismatch",
                $"PayPal reported {amount:0.00} {currency}, expected {expected:0.00} {order.Currency}.");
        }
    }

    private static PaymentOperationException InvalidState(Order order, string operation) =>
        new(409, "invalid_order_state",
            $"Order {order.Id} cannot {operation} while its payment state is {order.PaymentStatus}.");

    private static string StableRequestId(string input) =>
        "eshop-" + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(input)))
            .ToLowerInvariant();

    private static string AuthorizeRequestId(string paymentReference, PayOrderRequest request,
        SavedPaymentMethod? savedMethod)
    {
        // A hash makes exact retries stable without retaining or exposing raw card data.
        var paymentSource = savedMethod != null
            ? $"vault:{savedMethod.Id}:{savedMethod.PayPalPaymentTokenId}"
            : $"card:{request.Card!.Number}:{request.Card.Expiry}:{request.Card.SecurityCode}";
        return StableRequestId($"authorize:{paymentReference}:{paymentSource}");
    }

    private static void AddId(IDictionary<string, int> target, string? id, int orderId)
    {
        if (!string.IsNullOrWhiteSpace(id)) target[id] = orderId;
    }

    private static ReconciliationEShopRecord LocalRecord(Order order, string kind, string id,
        string status, decimal amount, DateTimeOffset at, ISet<string> payPalIds) =>
        new(order.Id, kind, id, status, amount, order.Currency, at, payPalIds.Contains(id));
}
