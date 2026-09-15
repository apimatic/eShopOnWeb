using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Payments;
using Microsoft.eShopWeb.Infrastructure.Data;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.Infrastructure.Payments;

public sealed record OrderLineCommand(int CatalogItemId, int Quantity);
public sealed record ShippingAddressCommand(string Street, string City, string State,
    string Country, string ZipCode);
public sealed record PlaceOrderCommand(IReadOnlyCollection<OrderLineCommand> Items,
    ShippingAddressCommand ShippingAddress);

public sealed record PaymentSelection(PaymentCard? Card, int? PaymentMethodId);

public sealed record RefundResult(Order Order, PaymentRefund Refund, bool Replayed);

public sealed record LocalPaymentRecord(int OrderId, string Type, string PayPalId, string Status,
    decimal? Amount, string? Currency, DateTimeOffset? Timestamp, bool FoundAtPayPal);

public sealed record ReconciledPayPalTransaction(GatewayTransaction Transaction, int? OrderId,
    string ReconciliationStatus);

public sealed record ReconciliationReport(DateTimeOffset From, DateTimeOffset To,
    IReadOnlyList<ReconciledPayPalTransaction> PayPalTransactions,
    IReadOnlyList<LocalPaymentRecord> EshopPayments);

public sealed class CommercePaymentService
{
    private static readonly ConcurrentDictionary<string, SemaphoreSlim> OperationLocks = new();
    private static readonly string[] AuthorizationCannotBeRenewedIssues =
    {
        "AUTHORIZATION_EXPIRED",
        "AUTHORIZATION_VOIDED",
        "AUTHORIZATION_ALREADY_COMPLETED",
        "MAX_NUMBER_OF_PAYMENT_ATTEMPTS_EXCEEDED",
        "PAYMENT_DENIED",
        "TRANSACTION_REFUSED"
    };

    private readonly CatalogContext _db;
    private readonly IPaymentGateway _gateway;
    private readonly PayPalOptions _options;

    public CommercePaymentService(CatalogContext db, IPaymentGateway gateway,
        IOptions<PayPalOptions> options)
    {
        _db = db;
        _gateway = gateway;
        _options = options.Value;
    }

    public async Task<Order> PlaceOrderAsync(string buyerId, PlaceOrderCommand command,
        CancellationToken cancellationToken)
    {
        if (command.Items.Count == 0)
        {
            throw BadRequest("EMPTY_ORDER", "An order must contain at least one catalog item.");
        }

        if (command.Items.Any(item => item.CatalogItemId <= 0 || item.Quantity <= 0))
        {
            throw BadRequest("INVALID_ORDER_ITEM", "Catalog item IDs and quantities must be positive.");
        }

        var quantities = command.Items
            .GroupBy(item => item.CatalogItemId)
            .ToDictionary(group => group.Key, group => group.Sum(item => item.Quantity));
        if (quantities.Values.Any(quantity => quantity > 1000))
        {
            throw BadRequest("INVALID_QUANTITY", "An item quantity cannot exceed 1000.");
        }

        var ids = quantities.Keys.ToArray();
        var catalogItems = await _db.CatalogItems.AsNoTracking()
            .Where(item => ids.Contains(item.Id))
            .ToListAsync(cancellationToken);
        if (catalogItems.Count != ids.Length)
        {
            var missing = ids.Except(catalogItems.Select(item => item.Id));
            throw BadRequest("CATALOG_ITEM_NOT_FOUND",
                $"Catalog item(s) {string.Join(", ", missing)} do not exist.");
        }

        var orderItems = catalogItems
            .OrderBy(item => item.Id)
            .Select(item => new OrderItem(
                new CatalogItemOrdered(item.Id, item.Name, item.PictureUri),
                item.Price,
                quantities[item.Id]))
            .ToList();
        var address = new Address(command.ShippingAddress.Street, command.ShippingAddress.City,
            command.ShippingAddress.State, command.ShippingAddress.Country,
            command.ShippingAddress.ZipCode);
        var order = new Order(buyerId, address, orderItems);
        _db.Orders.Add(order);
        await _db.SaveChangesAsync(cancellationToken);
        return order;
    }

    public async Task<Order> PayAsync(string buyerId, int orderId, PaymentSelection selection,
        CancellationToken cancellationToken)
    {
        return await LockedAsync($"order:{orderId}", async () =>
        {
            var order = await GetOrderAsync(orderId, cancellationToken);
            EnsureOwner(order, buyerId);
            if (order.Status == OrderStatus.Authorized) return order;
            if (order.Status is not (OrderStatus.AwaitingPayment or OrderStatus.PaymentRequired))
            {
                throw Conflict("ORDER_NOT_PAYABLE", $"Order {orderId} cannot be paid while it is {order.Status}.");
            }

            if ((selection.Card is null) == (selection.PaymentMethodId is null))
            {
                throw BadRequest("INVALID_PAYMENT_SOURCE",
                    "Supply either card details or paymentMethodId, but not both.");
            }

            PaymentSource source;
            if (selection.Card is not null)
            {
                source = PaymentSource.FromCard(selection.Card);
            }
            else
            {
                var paymentMethod = await _db.SavedPaymentMethods.AsNoTracking()
                    .SingleOrDefaultAsync(method => method.Id == selection.PaymentMethodId &&
                                                    method.BuyerId == buyerId, cancellationToken);
                if (paymentMethod is null)
                {
                    throw NotFound("PAYMENT_METHOD_NOT_FOUND", "The saved payment method was not found.");
                }

                source = PaymentSource.FromVault(paymentMethod.PayPalPaymentTokenId);
            }

            var total = Money(order.Total());
            var paypalOrderId = order.Status == OrderStatus.AwaitingPayment
                ? order.PayPalOrderId
                : null;
            if (string.IsNullOrWhiteSpace(paypalOrderId))
            {
                paypalOrderId = await _gateway.CreateOrderAsync(order.Id, order.PaymentReference, total, Currency,
                    RequestId("order", order.PaymentReference), cancellationToken);
                order.BeginPayment(Currency, paypalOrderId, "CREATED");
                await SaveGatewayResultAsync(cancellationToken);
            }

            GatewayAuthorization authorization;
            try
            {
                authorization = await _gateway.AuthorizeOrderAsync(paypalOrderId, source,
                    RequestId("authorize", order.PaymentReference), cancellationToken);
            }
            catch (PaymentGatewayException exception)
            {
                throw MapGatewayException(exception);
            }

            EnsureMoney(total, Currency, authorization.Amount, authorization.Currency,
                "PayPal authorization");
            order.RecordAuthorization(Currency, authorization.PayPalOrderId,
                authorization.PayPalOrderStatus, authorization.AuthorizationId,
                authorization.AuthorizationStatus, authorization.Amount,
                authorization.CreatedAt, authorization.ExpiresAt);
            await SaveGatewayResultAsync(cancellationToken);
            return order;
        }, cancellationToken);
    }

    public async Task<Order> FulfilAsync(int orderId, CancellationToken cancellationToken)
    {
        return await LockedAsync($"order:{orderId}", async () =>
        {
            var order = await GetOrderAsync(orderId, cancellationToken);
            if (order.Status is OrderStatus.Fulfilled or OrderStatus.PartiallyRefunded or OrderStatus.Refunded)
            {
                return order;
            }

            if (order.Status == OrderStatus.FulfilmentPending && order.CaptureId is not null)
            {
                GatewayCapture current;
                try
                {
                    current = await _gateway.GetCaptureAsync(order.CaptureId, cancellationToken);
                }
                catch (PaymentGatewayException exception)
                {
                    throw MapGatewayException(exception);
                }

                order.RecordCapture(current.Id, current.Status, current.Amount, current.PayPalFee,
                    current.NetAmount, current.CreatedAt);
                await SaveGatewayResultAsync(cancellationToken);
                if (order.Status == OrderStatus.PaymentRequired)
                {
                    throw Conflict("CAPTURE_NOT_COMPLETED",
                        $"PayPal reported capture status {current.Status}. Ask the shopper to pay again before retrying fulfilment.");
                }
                return order;
            }

            if (order.Status != OrderStatus.Authorized || order.AuthorizationId is null ||
                order.AuthorizationCreatedAt is null || order.OriginalAuthorizationCreatedAt is null)
            {
                throw Conflict("ORDER_NOT_AUTHORIZED",
                    $"Order {orderId} must have an active authorization before fulfilment.");
            }

            var now = DateTimeOffset.UtcNow;
            if (now >= order.OriginalAuthorizationCreatedAt.Value.AddDays(29))
            {
                await MarkPaymentRequiredAsync(order, "EXPIRED", cancellationToken);
                throw Conflict("AUTHORIZATION_CANNOT_BE_RENEWED",
                    "The PayPal authorization is outside its 29-day validity period. Ask the shopper to pay the order again, then retry fulfilment.");
            }

            var reauthorized = false;
            if (now >= order.AuthorizationCreatedAt.Value.AddDays(3))
            {
                await ReauthorizeAsync(order, cancellationToken);
                reauthorized = true;
            }

            GatewayCapture capture;
            try
            {
                capture = await CaptureAsync(order, cancellationToken);
            }
            catch (PaymentGatewayException exception) when (!reauthorized &&
                exception.HasIssue("AUTHORIZATION_EXPIRED"))
            {
                await ReauthorizeAsync(order, cancellationToken);
                try
                {
                    capture = await CaptureAsync(order, cancellationToken);
                }
                catch (PaymentGatewayException retryException)
                {
                    throw MapGatewayException(retryException);
                }
            }
            catch (PaymentGatewayException exception)
            {
                throw MapGatewayException(exception);
            }

            EnsureMoney(Money(order.Total()), Currency, capture.Amount, capture.Currency,
                "PayPal capture");
            order.RecordCapture(capture.Id, capture.Status, capture.Amount, capture.PayPalFee,
                capture.NetAmount, capture.CreatedAt);
            await SaveGatewayResultAsync(cancellationToken);
            if (order.Status == OrderStatus.PaymentRequired)
            {
                throw Conflict("CAPTURE_NOT_COMPLETED",
                    $"PayPal reported capture status {capture.Status}. Ask the shopper to pay again before retrying fulfilment.");
            }
            return order;
        }, cancellationToken);
    }

    public async Task<Order> CancelAsync(int orderId, CancellationToken cancellationToken)
    {
        return await LockedAsync($"order:{orderId}", async () =>
        {
            var order = await GetOrderAsync(orderId, cancellationToken);
            if (order.Status == OrderStatus.Cancelled) return order;
            if (order.Status is OrderStatus.Fulfilled or OrderStatus.FulfilmentPending or
                OrderStatus.PartiallyRefunded or OrderStatus.Refunded)
            {
                throw Conflict("ORDER_ALREADY_CAPTURED",
                    "A captured order cannot be cancelled. Use a refund instead.");
            }

            var authorizationStatus = order.AuthorizationStatus ?? "NOT_AUTHORIZED";
            if (order.Status == OrderStatus.Authorized && order.AuthorizationId is not null)
            {
                try
                {
                    await _gateway.VoidAsync(order.AuthorizationId,
                        RequestId("void", order.PaymentReference), cancellationToken);
                    authorizationStatus = "VOIDED";
                }
                catch (PaymentGatewayException exception) when (
                    exception.HasIssue("AUTHORIZATION_ALREADY_VOIDED") ||
                    exception.HasIssue("PREVIOUSLY_VOIDED") ||
                    exception.HasIssue("AUTHORIZATION_EXPIRED"))
                {
                    authorizationStatus = "VOIDED";
                }
                catch (PaymentGatewayException exception)
                {
                    throw MapGatewayException(exception);
                }
            }

            order.Cancel(authorizationStatus);
            await SaveGatewayResultAsync(cancellationToken);
            return order;
        }, cancellationToken);
    }

    public async Task<RefundResult> RefundAsync(string buyerId, int orderId, decimal? requestedAmount,
        string idempotencyKey, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(idempotencyKey) || idempotencyKey.Length > 128)
        {
            throw BadRequest("INVALID_IDEMPOTENCY_KEY",
                "idempotencyKey is required and cannot exceed 128 characters.");
        }

        return await LockedAsync($"order:{orderId}", async () =>
        {
            var order = await GetOrderAsync(orderId, cancellationToken);
            EnsureOwner(order, buyerId);
            var existing = order.Refunds.SingleOrDefault(refund =>
                refund.IdempotencyKey == idempotencyKey);
            if (existing is not null)
            {
                if (requestedAmount is not null && Money(requestedAmount.Value) != existing.Amount)
                {
                    throw Conflict("IDEMPOTENCY_KEY_REUSED",
                        "This idempotency key was already used with a different refund amount.");
                }

                return new RefundResult(order, existing, true);
            }

            if (order.CapturedAmount is null || order.CaptureId is null ||
                order.Status is not (OrderStatus.Fulfilled or OrderStatus.PartiallyRefunded))
            {
                throw Conflict("ORDER_NOT_REFUNDABLE", "The order does not have a refundable capture.");
            }

            var remaining = order.CapturedAmount.Value - order.RefundedAmount;
            var amount = requestedAmount is null ? remaining : Money(requestedAmount.Value);
            if (amount <= 0 || amount > remaining ||
                (requestedAmount is not null && requestedAmount.Value != amount))
            {
                throw BadRequest("INVALID_REFUND_AMOUNT",
                    $"Refund amount must be positive, have at most two decimal places, and not exceed {remaining:0.00} {Currency}.");
            }

            GatewayRefund gatewayRefund;
            try
            {
                gatewayRefund = await _gateway.RefundAsync(order.CaptureId, amount, Currency,
                    RequestId("refund", $"{order.PaymentReference}:{idempotencyKey}"), cancellationToken);
            }
            catch (PaymentGatewayException exception)
            {
                throw MapGatewayException(exception);
            }

            EnsureMoney(amount, Currency, gatewayRefund.Amount, gatewayRefund.Currency,
                "PayPal refund");
            var refund = order.RecordRefund(gatewayRefund.Id, idempotencyKey, gatewayRefund.Status,
                gatewayRefund.Amount, gatewayRefund.CreatedAt);
            await SaveGatewayResultAsync(cancellationToken);
            return new RefundResult(order, refund, false);
        }, cancellationToken);
    }

    public async Task<IReadOnlyList<Order>> GetMyOrdersAsync(string buyerId,
        CancellationToken cancellationToken) =>
        await _db.Orders.AsNoTracking()
            .Where(order => order.BuyerId == buyerId)
            .Include(order => order.OrderItems)
            .Include(order => order.Refunds)
            .OrderByDescending(order => order.OrderDate)
            .ToListAsync(cancellationToken);

    public async Task<SavedPaymentMethod> SavePaymentMethodAsync(string buyerId, PaymentCard card,
        CancellationToken cancellationToken)
    {
        var requestId = RequestId("vault", $"{buyerId}:{Guid.NewGuid():N}");
        GatewaySavedCard saved;
        try
        {
            saved = await _gateway.SaveCardAsync(card, requestId, cancellationToken);
        }
        catch (PaymentGatewayException exception)
        {
            throw MapGatewayException(exception);
        }

        var paymentMethod = new SavedPaymentMethod(buyerId, saved.PaymentTokenId,
            saved.CustomerId, saved.Brand, saved.Last4, saved.Expiry);
        _db.SavedPaymentMethods.Add(paymentMethod);
        try
        {
            await _db.SaveChangesAsync(cancellationToken);
        }
        catch
        {
            try
            {
                await _gateway.DeletePaymentTokenAsync(saved.PaymentTokenId,
                    RequestId("delete-vault", saved.PaymentTokenId), CancellationToken.None);
            }
            catch
            {
                // Preserve the original persistence failure; reconciliation can identify the orphan.
            }

            throw;
        }

        return paymentMethod;
    }

    public async Task<IReadOnlyList<SavedPaymentMethod>> GetPaymentMethodsAsync(string buyerId,
        CancellationToken cancellationToken) =>
        await _db.SavedPaymentMethods.AsNoTracking()
            .Where(method => method.BuyerId == buyerId)
            .OrderByDescending(method => method.CreatedAt)
            .ToListAsync(cancellationToken);

    public async Task DeletePaymentMethodAsync(string buyerId, int paymentMethodId,
        CancellationToken cancellationToken)
    {
        await LockedAsync($"method:{paymentMethodId}", async () =>
        {
            var paymentMethod = await _db.SavedPaymentMethods.SingleOrDefaultAsync(method =>
                method.Id == paymentMethodId && method.BuyerId == buyerId, cancellationToken);
            if (paymentMethod is null)
            {
                throw NotFound("PAYMENT_METHOD_NOT_FOUND", "The saved payment method was not found.");
            }

            try
            {
                await _gateway.DeletePaymentTokenAsync(paymentMethod.PayPalPaymentTokenId,
                    RequestId("delete-vault", paymentMethod.PayPalPaymentTokenId), cancellationToken);
            }
            catch (PaymentGatewayException exception)
            {
                throw MapGatewayException(exception);
            }

            _db.SavedPaymentMethods.Remove(paymentMethod);
            await _db.SaveChangesAsync(cancellationToken);
        }, cancellationToken);
    }

    public async Task<ReconciliationReport> ReconcileAsync(DateTimeOffset from, DateTimeOffset to,
        CancellationToken cancellationToken)
    {
        if (from >= to)
        {
            throw BadRequest("INVALID_DATE_RANGE", "from must be earlier than to.");
        }

        IReadOnlyList<GatewayTransaction> paypalTransactions;
        try
        {
            paypalTransactions = await _gateway.SearchTransactionsAsync(from, to, cancellationToken);
        }
        catch (PaymentGatewayException exception)
        {
            throw MapGatewayException(exception);
        }

        var orders = await _db.Orders.AsNoTracking()
            .Include(order => order.Refunds)
            .Where(order => order.AuthorizationCreatedAt < to || order.CapturedAt < to ||
                            order.Refunds.Any(refund => refund.CreatedAt < to))
            .ToListAsync(cancellationToken);

        var idToOrder = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var order in orders)
        {
            AddId(idToOrder, order.PayPalOrderId, order.Id);
            AddId(idToOrder, $"ESHOP-{order.PaymentReference}", order.Id);
            AddId(idToOrder, order.AuthorizationId, order.Id);
            AddId(idToOrder, order.CaptureId, order.Id);
            foreach (var refund in order.Refunds) AddId(idToOrder, refund.PayPalRefundId, order.Id);
        }

        var reconciled = paypalTransactions.Select(transaction =>
        {
            int? orderId = null;
            if (idToOrder.TryGetValue(transaction.TransactionId, out var direct)) orderId = direct;
            else if (transaction.PayPalReferenceId is not null &&
                     idToOrder.TryGetValue(transaction.PayPalReferenceId, out var referenced)) orderId = referenced;
            else if (transaction.InvoiceId is not null &&
                     idToOrder.TryGetValue(transaction.InvoiceId, out var invoiced)) orderId = invoiced;
            return new ReconciledPayPalTransaction(transaction, orderId,
                orderId is null ? "PayPalOnly" : "Matched");
        }).ToList();

        var paypalIds = paypalTransactions
            .SelectMany(transaction => new[] { transaction.TransactionId, transaction.PayPalReferenceId })
            .Where(id => id is not null)
            .Select(id => id!)
            .ToHashSet(StringComparer.Ordinal);
        var local = new List<LocalPaymentRecord>();
        foreach (var order in orders)
        {
            AddLocal(local, order.Id, "Authorization", order.AuthorizationId, order.AuthorizationStatus,
                order.AuthorizedAmount, order.PaymentCurrency, order.AuthorizationCreatedAt,
                from, to, paypalIds);
            AddLocal(local, order.Id, "Capture", order.CaptureId, order.CaptureStatus,
                order.CapturedAmount, order.PaymentCurrency, order.CapturedAt,
                from, to, paypalIds);
            foreach (var refund in order.Refunds)
            {
                AddLocal(local, order.Id, "Refund", refund.PayPalRefundId, refund.Status,
                    refund.Amount, order.PaymentCurrency, refund.CreatedAt,
                    from, to, paypalIds);
            }
        }

        return new ReconciliationReport(from, to, reconciled, local);
    }

    private async Task ReauthorizeAsync(Order order, CancellationToken cancellationToken)
    {
        if (order.AuthorizationId is null) throw new InvalidOperationException();
        GatewayAuthorization renewed;
        try
        {
            renewed = await _gateway.ReauthorizeAsync(order.AuthorizationId, Money(order.Total()),
                Currency, RequestId("reauthorize", $"{order.Id}:{order.AuthorizationId}"),
                cancellationToken);
        }
        catch (PaymentGatewayException exception) when (
            exception.StatusCode is >= 400 and < 500 ||
            AuthorizationCannotBeRenewedIssues.Any(exception.HasIssue))
        {
            await MarkPaymentRequiredAsync(order, "REAUTHORIZATION_FAILED", cancellationToken);
            throw Conflict("AUTHORIZATION_CANNOT_BE_RENEWED",
                "PayPal can no longer renew this authorization. Ask the shopper to pay the order again, then retry fulfilment.");
        }
        catch (PaymentGatewayException exception)
        {
            throw MapGatewayException(exception);
        }

        EnsureMoney(Money(order.Total()), Currency, renewed.Amount, renewed.Currency,
            "PayPal reauthorization");
        order.RecordReauthorization(renewed.AuthorizationId, renewed.AuthorizationStatus,
            renewed.Amount, renewed.CreatedAt, renewed.ExpiresAt);
        await SaveGatewayResultAsync(cancellationToken);
    }

    private async Task<GatewayCapture> CaptureAsync(Order order, CancellationToken cancellationToken)
    {
        if (order.AuthorizationId is null) throw new InvalidOperationException();
        return await _gateway.CaptureAsync(order.AuthorizationId, order.Id, order.PaymentReference,
            Money(order.Total()), Currency, RequestId("capture", order.PaymentReference),
            cancellationToken);
    }

    private async Task MarkPaymentRequiredAsync(Order order, string status,
        CancellationToken cancellationToken)
    {
        order.RequireNewPayment(status);
        await _db.SaveChangesAsync(cancellationToken);
    }

    private async Task SaveGatewayResultAsync(CancellationToken cancellationToken)
    {
        try
        {
            await _db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            throw Conflict("CONCURRENT_PAYMENT_UPDATE",
                "PayPal completed the operation while another request updated this order. Retry the same API request; its idempotency key prevents duplicate money movement.");
        }
    }

    private async Task<Order> GetOrderAsync(int orderId, CancellationToken cancellationToken)
    {
        var order = await _db.Orders
            .Include(current => current.OrderItems)
            .Include(current => current.Refunds)
            .SingleOrDefaultAsync(current => current.Id == orderId, cancellationToken);
        return order ?? throw NotFound("ORDER_NOT_FOUND", $"Order {orderId} was not found.");
    }

    private static void EnsureOwner(Order order, string buyerId)
    {
        if (!string.Equals(order.BuyerId, buyerId, StringComparison.Ordinal))
        {
            // Do not reveal whether another shopper's order exists.
            throw NotFound("ORDER_NOT_FOUND", $"Order {order.Id} was not found.");
        }
    }

    private static void EnsureMoney(decimal expectedAmount, string expectedCurrency,
        decimal actualAmount, string actualCurrency, string operation)
    {
        if (expectedAmount != actualAmount ||
            !string.Equals(expectedCurrency, actualCurrency, StringComparison.OrdinalIgnoreCase))
        {
            throw new CommerceException(502, "PAYPAL_AMOUNT_MISMATCH",
                $"{operation} returned an amount or currency that does not match the order.");
        }
    }

    private string Currency => _options.Currency.ToUpperInvariant();
    private static decimal Money(decimal value) => Math.Round(value, 2, MidpointRounding.AwayFromZero);

    private static string RequestId(string operation, string key)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes($"eshop:{operation}:{key}"));
        return Convert.ToHexString(hash).ToLowerInvariant()[..32];
    }

    private static CommerceException MapGatewayException(PaymentGatewayException exception)
    {
        if (exception.Code == "PAYER_ACTION_REQUIRED" || exception.HasIssue("PAYER_ACTION_REQUIRED"))
        {
            return new CommerceException(422, "PAYER_ACTION_REQUIRED",
                "PayPal requires a browser challenge for this card. This headless payment flow cannot continue.");
        }

        var status = exception.StatusCode switch
        {
            400 or 404 or 409 or 422 => exception.StatusCode,
            401 or 403 => 502,
            _ => 502
        };
        return new CommerceException(status, exception.Code, exception.Message);
    }

    private static CommerceException BadRequest(string code, string message) => new(400, code, message);
    private static CommerceException NotFound(string code, string message) => new(404, code, message);
    private static CommerceException Conflict(string code, string message) => new(409, code, message);

    private static async Task<T> LockedAsync<T>(string key, Func<Task<T>> action,
        CancellationToken cancellationToken)
    {
        var gate = OperationLocks.GetOrAdd(key, _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(cancellationToken);
        try { return await action(); }
        finally { gate.Release(); }
    }

    private static async Task LockedAsync(string key, Func<Task> action,
        CancellationToken cancellationToken)
    {
        var gate = OperationLocks.GetOrAdd(key, _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(cancellationToken);
        try { await action(); }
        finally { gate.Release(); }
    }

    private static void AddId(IDictionary<string, int> ids, string? id, int orderId)
    {
        if (!string.IsNullOrWhiteSpace(id)) ids[id] = orderId;
    }

    private static void AddLocal(ICollection<LocalPaymentRecord> records, int orderId, string type,
        string? id, string? status, decimal? amount, string? currency, DateTimeOffset? timestamp,
        DateTimeOffset from, DateTimeOffset to, IReadOnlySet<string> paypalIds)
    {
        if (id is null || timestamp is null || timestamp < from || timestamp >= to) return;
        records.Add(new LocalPaymentRecord(orderId, type, id, status ?? "UNKNOWN", amount,
            currency, timestamp, paypalIds.Contains(id)));
    }
}
