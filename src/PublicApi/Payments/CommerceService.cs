using System.Collections.Concurrent;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.Infrastructure.Data;
using Microsoft.Extensions.Options;
using SavedPaymentMethod = Microsoft.eShopWeb.ApplicationCore.Entities.BuyerAggregate.PaymentMethod;

namespace Microsoft.eShopWeb.PublicApi.Payments;

public sealed class CommerceService
{
    private static readonly ConcurrentDictionary<string, SemaphoreSlim> OperationLocks = new();

    private readonly CatalogContext _db;
    private readonly IPayPalClient _payPal;
    private readonly PayPalOptions _options;

    public CommerceService(CatalogContext db, IPayPalClient payPal, IOptions<PayPalOptions> options)
    {
        _db = db;
        _payPal = payPal;
        _options = options.Value;
    }

    public async Task<PlaceOrderResponse> PlaceOrderAsync(string buyerId, PlaceOrderRequest request,
        CancellationToken cancellationToken)
    {
        if (request.Items is null || request.Items.Count == 0)
            throw CommerceException.BadRequest("EMPTY_ORDER", "At least one catalog item is required.");
        if (request.ShippingAddress is null)
            throw CommerceException.BadRequest("SHIPPING_ADDRESS_REQUIRED", "A shipping address is required.");
        if (request.Items.Any(x => x.CatalogItemId <= 0 || x.Quantity <= 0))
            throw CommerceException.BadRequest("INVALID_ITEM", "Catalog item IDs and quantities must be positive.");
        if (request.Items.Select(x => x.CatalogItemId).Distinct().Count() != request.Items.Count)
            throw CommerceException.BadRequest("DUPLICATE_ITEM", "Each catalog item may appear only once.");

        var ids = request.Items.Select(x => x.CatalogItemId).ToArray();
        var catalogItems = await _db.CatalogItems
            .Where(x => ids.Contains(x.Id))
            .ToDictionaryAsync(x => x.Id, cancellationToken);
        var missing = ids.Where(id => !catalogItems.ContainsKey(id)).ToArray();
        if (missing.Length != 0)
            throw CommerceException.BadRequest("CATALOG_ITEM_NOT_FOUND",
                $"Catalog item(s) {string.Join(", ", missing)} do not exist.");

        var items = request.Items.Select(requestItem =>
        {
            var catalogItem = catalogItems[requestItem.CatalogItemId];
            var itemOrdered = new CatalogItemOrdered(catalogItem.Id, catalogItem.Name,
                string.IsNullOrWhiteSpace(catalogItem.PictureUri) ? "images/products/eCatalog-item-default.png" : catalogItem.PictureUri);
            return new OrderItem(itemOrdered, decimal.Round(catalogItem.Price, 2), requestItem.Quantity);
        }).ToList();
        var shipping = request.ShippingAddress;
        ValidateAddress(shipping);
        var order = new Order(buyerId,
            new Address(shipping.Street, shipping.City, shipping.State, shipping.Country, shipping.ZipCode),
            items);
        if (order.Total() <= 0)
            throw CommerceException.BadRequest("INVALID_ORDER_TOTAL", "The order total must be positive.");

        _db.Orders.Add(order);
        await _db.SaveChangesAsync(cancellationToken);
        return new PlaceOrderResponse(order.Id, order.PaymentStatus.ToString(), order.Total());
    }

    public async Task<OrderResponse> PayAsync(string buyerId, int orderId, PayOrderRequest request,
        CancellationToken cancellationToken)
    {
        await using var operationLock = await AcquireAsync($"pay:{orderId}", cancellationToken);
        var order = await LoadOrderAsync(orderId, cancellationToken);
        EnsureOwned(order, buyerId);
        if (order.PaymentStatus == PaymentStatus.Authorized) return Map(order);
        if (order.PaymentStatus != PaymentStatus.AwaitingPayment)
            throw CommerceException.Conflict("ORDER_NOT_PAYABLE",
                $"An order in state '{order.PaymentStatus}' cannot be paid.");
        ValidatePaymentChoice(request);

        string? vaultId = null;
        if (request.PaymentMethodId is not null)
        {
            var method = await _db.PaymentMethods.SingleOrDefaultAsync(
                x => x.Id == request.PaymentMethodId.Value && x.OwnerId == buyerId, cancellationToken);
            if (method is null)
                throw CommerceException.NotFound("The saved payment method was not found.");
            vaultId = method.VaultId;
        }
        else
        {
            ValidateCard(request.Card!);
        }

        var currency = Currency();
        var authorization = await _payPal.AuthorizeOrderAsync(order.PaymentReference, order.Total(), currency,
            request.Card, vaultId, RequestId($"order:{order.PaymentReference}:authorize"), cancellationToken);
        if (authorization.PayerActionRequired) throw new PayPalPayerActionRequiredException();
        EnsureMoney(order.Total(), currency, authorization.Amount, authorization.Currency,
            "authorization");
        if (authorization.Status is not ("CREATED" or "PENDING"))
            throw CommerceException.Conflict("AUTHORIZATION_NOT_USABLE",
                $"PayPal returned authorization status '{authorization.Status}'.");

        order.RecordAuthorization(currency, authorization.PayPalOrderId,
            authorization.PayPalOrderStatus, authorization.AuthorizationId, authorization.Status,
            authorization.Amount, authorization.CreatedAt, authorization.ExpiresAt);
        await _db.SaveChangesAsync(cancellationToken);
        return Map(order);
    }

    public async Task<OrderResponse> FulfilAsync(int orderId, CancellationToken cancellationToken)
    {
        await using var operationLock = await AcquireAsync($"fulfil:{orderId}", cancellationToken);
        var order = await LoadOrderAsync(orderId, cancellationToken);
        if (order.PaymentStatus is PaymentStatus.Fulfilled or PaymentStatus.PartiallyRefunded or PaymentStatus.Refunded)
            return Map(order);

        if (order.PaymentStatus == PaymentStatus.CapturePending)
        {
            var refreshed = await _payPal.GetCaptureAsync(order.CaptureId!, cancellationToken);
            EnsureMoney(order.Total(), order.Currency!, refreshed.Amount, refreshed.Currency, "capture");
            order.UpdateCapture(refreshed.Status, refreshed.Amount, refreshed.Fee, refreshed.Net,
                refreshed.CreatedAt);
            await _db.SaveChangesAsync(cancellationToken);
            return Map(order);
        }

        if (order.PaymentStatus != PaymentStatus.Authorized)
            throw CommerceException.Conflict("ORDER_NOT_FULFILLABLE",
                $"An order in state '{order.PaymentStatus}' cannot be fulfilled.");

        if (order.AuthorizationStatus == "PENDING")
        {
            var refreshedAuthorization = await _payPal.GetAuthorizationAsync(order.AuthorizationId!,
                cancellationToken);
            order.UpdateAuthorizationStatus(refreshedAuthorization.Status);
            await _db.SaveChangesAsync(cancellationToken);
            if (refreshedAuthorization.Status != "CREATED")
                throw CommerceException.Conflict("AUTHORIZATION_NOT_READY",
                    $"PayPal reports authorization status '{refreshedAuthorization.Status}'. Wait for it to become CREATED, or ask the shopper to authorize again if it is denied.");
        }

        var now = DateTimeOffset.UtcNow;
        var authorizationId = order.AuthorizationId!;
        if (order.AuthorizationCreatedAt is not null && now >= order.AuthorizationCreatedAt.Value.AddDays(3))
        {
            var cannotRenewAfter = order.AuthorizationExpiresAt ?? order.AuthorizationCreatedAt.Value.AddDays(29);
            if (now >= cannotRenewAfter)
                throw CommerceException.Conflict("AUTHORIZATION_CANNOT_BE_RENEWED",
                    "The PayPal authorization has expired and is outside its renewal window. Ask the shopper to authorize the order again before fulfilment.");

            try
            {
                var renewed = await _payPal.ReauthorizeAsync(authorizationId, order.Total(),
                    order.Currency!, RequestId($"order:{order.PaymentReference}:reauthorize:{authorizationId}"),
                    cancellationToken);
                EnsureMoney(order.Total(), order.Currency!, renewed.Amount, renewed.Currency,
                    "reauthorization");
                order.RecordReauthorization(renewed.AuthorizationId, renewed.Status, renewed.Amount,
                    renewed.CreatedAt, renewed.ExpiresAt);
                authorizationId = renewed.AuthorizationId;
                await _db.SaveChangesAsync(cancellationToken);
            }
            catch (PayPalApiException ex) when (ex.StatusCode is 404 or 422)
            {
                throw CommerceException.Conflict("AUTHORIZATION_CANNOT_BE_RENEWED",
                    $"PayPal can no longer renew this authorization ({string.Join(", ", ex.Issues.DefaultIfEmpty(ex.ErrorName))}). Ask the shopper to authorize the order again.");
            }
        }

        var capture = await _payPal.CaptureAsync(authorizationId, order.Total(), order.Currency!,
            order.PaymentReference, RequestId($"order:{order.PaymentReference}:capture"), cancellationToken);
        EnsureMoney(order.Total(), order.Currency!, capture.Amount, capture.Currency, "capture");
        order.RecordCapture(capture.Id, capture.Status, capture.Amount, capture.Fee, capture.Net,
            capture.CreatedAt);
        await _db.SaveChangesAsync(cancellationToken);
        return Map(order);
    }

    public async Task<OrderResponse> CancelAsync(int orderId, CancellationToken cancellationToken)
    {
        await using var operationLock = await AcquireAsync($"cancel:{orderId}", cancellationToken);
        var order = await LoadOrderAsync(orderId, cancellationToken);
        if (order.PaymentStatus == PaymentStatus.Cancelled) return Map(order);
        if (order.PaymentStatus is not (PaymentStatus.AwaitingPayment or PaymentStatus.Authorized))
            throw CommerceException.Conflict("ORDER_NOT_CANCELLABLE",
                $"An order in state '{order.PaymentStatus}' cannot be cancelled.");

        string? authorizationStatus = null;
        if (order.PaymentStatus == PaymentStatus.Authorized)
        {
            authorizationStatus = await _payPal.VoidAsync(order.AuthorizationId!,
                RequestId($"order:{order.PaymentReference}:void"), cancellationToken);
        }
        order.Cancel(authorizationStatus, DateTimeOffset.UtcNow);
        await _db.SaveChangesAsync(cancellationToken);
        return Map(order);
    }

    public async Task<RefundOrderResponse> RefundAsync(string buyerId, int orderId,
        RefundOrderRequest request, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.IdempotencyKey) || request.IdempotencyKey.Length > 80)
            throw CommerceException.BadRequest("INVALID_IDEMPOTENCY_KEY",
                "IdempotencyKey is required and must be at most 80 characters.");

        await using var operationLock = await AcquireAsync($"refund:{orderId}", cancellationToken);
        var order = await LoadOrderAsync(orderId, cancellationToken);
        EnsureOwned(order, buyerId);
        foreach (var pending in order.Refunds.Where(x => x.Status == "PENDING").ToArray())
        {
            var refreshed = await _payPal.GetRefundAsync(pending.PayPalRefundId, cancellationToken);
            order.UpdateRefundStatus(pending.PayPalRefundId, refreshed.Status);
        }
        if (_db.ChangeTracker.HasChanges()) await _db.SaveChangesAsync(cancellationToken);
        var existing = order.Refunds.SingleOrDefault(x => x.IdempotencyKey == request.IdempotencyKey);
        if (existing is not null)
        {
            if (request.Amount is not null && decimal.Round(request.Amount.Value, 2) != existing.Amount)
                throw CommerceException.Conflict("IDEMPOTENCY_KEY_REUSED",
                    "That idempotency key was already used with a different refund amount.");
            return MapRefund(order, existing);
        }

        if (order.PaymentStatus is not (PaymentStatus.Fulfilled or PaymentStatus.PartiallyRefunded))
            throw CommerceException.Conflict("ORDER_NOT_REFUNDABLE",
                $"An order in state '{order.PaymentStatus}' cannot be refunded.");
        var remaining = order.CapturedAmount!.Value - order.RefundedAmount;
        var amount = request.Amount is null ? remaining : decimal.Round(request.Amount.Value, 2);
        if (amount <= 0 || amount > remaining)
            throw CommerceException.BadRequest("INVALID_REFUND_AMOUNT",
                $"Refund amount must be positive and no greater than {remaining:0.00} {order.Currency}.");

        var paypalRefund = await _payPal.RefundAsync(order.CaptureId!, amount, order.Currency!,
            RequestId($"order:{order.PaymentReference}:refund:{request.IdempotencyKey}"),
            order.PaymentReference + "-REFUND", cancellationToken);
        EnsureMoney(amount, order.Currency!, paypalRefund.Amount, paypalRefund.Currency, "refund");
        var refund = order.RecordRefund(request.IdempotencyKey, paypalRefund.Id, paypalRefund.Status,
            paypalRefund.Amount, paypalRefund.CreatedAt);
        await _db.SaveChangesAsync(cancellationToken);
        return MapRefund(order, refund);
    }

    public async Task<IReadOnlyList<OrderResponse>> GetMyOrdersAsync(string buyerId,
        CancellationToken cancellationToken)
    {
        var orders = await _db.Orders.AsNoTracking()
            .Include(x => x.OrderItems)
            .Include(x => x.Refunds)
            .Where(x => x.BuyerId == buyerId)
            .OrderByDescending(x => x.OrderDate)
            .ToListAsync(cancellationToken);
        return orders.Select(Map).ToArray();
    }

    public async Task<SavePaymentMethodResponse> SavePaymentMethodAsync(string ownerId,
        SavePaymentMethodRequest request, CancellationToken cancellationToken)
    {
        ValidateCard(request.Card);
        var requestId = RequestId($"vault:{ownerId}:{Guid.NewGuid():N}");
        var customerId = CustomerId(ownerId);
        var token = await _payPal.CreatePaymentTokenAsync(customerId, request.Card, requestId,
            cancellationToken);
        var paymentMethod = new SavedPaymentMethod(ownerId, token.Id, token.Brand, token.Last4,
            token.Expiry, DateTimeOffset.UtcNow);
        _db.PaymentMethods.Add(paymentMethod);
        await _db.SaveChangesAsync(cancellationToken);
        return new SavePaymentMethodResponse(paymentMethod.Id, paymentMethod.Brand,
            paymentMethod.Last4, paymentMethod.Expiry);
    }

    public async Task<IReadOnlyList<PaymentMethodResponse>> GetPaymentMethodsAsync(string ownerId,
        CancellationToken cancellationToken) => await _db.PaymentMethods.AsNoTracking()
        .Where(x => x.OwnerId == ownerId)
        .OrderBy(x => x.Id)
        .Select(x => new PaymentMethodResponse(x.Id, x.Brand, x.Last4, x.Expiry, x.CreatedAt))
        .ToListAsync(cancellationToken);

    public async Task DeletePaymentMethodAsync(string ownerId, int paymentMethodId,
        CancellationToken cancellationToken)
    {
        await using var operationLock = await AcquireAsync($"payment-method:{paymentMethodId}",
            cancellationToken);
        var method = await _db.PaymentMethods.SingleOrDefaultAsync(
            x => x.Id == paymentMethodId && x.OwnerId == ownerId, cancellationToken);
        if (method is null) throw CommerceException.NotFound("The saved payment method was not found.");
        await _payPal.DeletePaymentTokenAsync(method.VaultId, cancellationToken);
        _db.PaymentMethods.Remove(method);
        await _db.SaveChangesAsync(cancellationToken);
    }

    public async Task<ReconciliationResponse> ReconcileAsync(DateTimeOffset from, DateTimeOffset to,
        CancellationToken cancellationToken)
    {
        if (from >= to)
            throw CommerceException.BadRequest("INVALID_DATE_RANGE", "'from' must be earlier than 'to'.");

        var transactions = await _payPal.SearchAllTransactionsAsync(from, to, cancellationToken);
        var orders = await _db.Orders.AsNoTracking().Include(x => x.Refunds)
            .Where(x => x.PayPalOrderId != null)
            .ToListAsync(cancellationToken);
        var resources = BuildResourceLookup(orders);
        var invoiceOrders = orders.ToDictionary(x => x.PaymentReference, x => x.Id,
            StringComparer.OrdinalIgnoreCase);
        var seenResources = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var rows = transactions.Select(transaction =>
        {
            int? orderId = null;
            if (resources.TryGetValue(transaction.TransactionId, out var direct))
            {
                orderId = direct.OrderId;
                seenResources.Add(transaction.TransactionId);
            }
            else if (transaction.ReferenceId is not null &&
                     resources.TryGetValue(transaction.ReferenceId, out var reference))
            {
                orderId = reference.OrderId;
                seenResources.Add(transaction.ReferenceId);
            }
            else if (transaction.InvoiceId is not null &&
                     invoiceOrders.TryGetValue(transaction.InvoiceId, out var invoiceOrderId))
            {
                orderId = invoiceOrderId;
            }

            return new ReconciliationTransactionResponse(transaction.TransactionId, orderId,
                orderId is null ? "PayPalOnly" : "Matched", transaction.ReferenceId,
                transaction.ReferenceIdType, transaction.EventCode, transaction.InitiatedAt,
                transaction.UpdatedAt, transaction.Amount, transaction.Currency, transaction.Fee,
                transaction.Status, transaction.InvoiceId);
        }).ToArray();

        var missing = resources
            .Where(x => x.Value.OccurredAt >= from && x.Value.OccurredAt <= to &&
                        !seenResources.Contains(x.Key) &&
                        !rows.Any(row => row.OrderId == x.Value.OrderId))
            .Select(x => new ReconciliationMissingOrderResponse(x.Value.OrderId,
                x.Value.PaymentState, x.Value.ResourceType, x.Key, x.Value.OccurredAt,
                x.Value.Amount, x.Value.Currency))
            .OrderBy(x => x.OccurredAt)
            .ToArray();
        return new ReconciliationResponse(from, to, rows, missing);
    }

    private async Task<Order> LoadOrderAsync(int orderId, CancellationToken cancellationToken) =>
        await _db.Orders.Include(x => x.OrderItems).Include(x => x.Refunds)
            .SingleOrDefaultAsync(x => x.Id == orderId, cancellationToken)
        ?? throw CommerceException.NotFound("The order was not found.");

    private static void EnsureOwned(Order order, string buyerId)
    {
        if (!string.Equals(order.BuyerId, buyerId, StringComparison.Ordinal))
            throw CommerceException.NotFound("The order was not found.");
    }

    private string Currency()
    {
        var currency = _options.Currency.Trim().ToUpperInvariant();
        if (currency.Length != 3)
            throw new InvalidOperationException("PayPal:Currency must be a three-character currency code.");
        return currency;
    }

    private static void ValidatePaymentChoice(PayOrderRequest request)
    {
        if ((request.Card is null) == (request.PaymentMethodId is null))
            throw CommerceException.BadRequest("INVALID_PAYMENT_SOURCE",
                "Provide either card details or paymentMethodId, but not both.");
        if (request.PaymentMethodId <= 0)
            throw CommerceException.BadRequest("INVALID_PAYMENT_SOURCE", "paymentMethodId must be positive.");
    }

    private static void ValidateCard(CardInput card)
    {
        var number = card.Number?.Replace(" ", string.Empty, StringComparison.Ordinal) ?? string.Empty;
        if (string.IsNullOrWhiteSpace(card.Name) || number.Length is < 13 or > 19 ||
            number.Any(x => !char.IsAsciiDigit(x)) ||
            !DateOnly.TryParseExact(card.Expiry + "-01", "yyyy-MM-dd", CultureInfo.InvariantCulture,
                DateTimeStyles.None, out var expiry) || expiry < new DateOnly(DateTime.UtcNow.Year, DateTime.UtcNow.Month, 1) ||
            card.SecurityCode is null || card.SecurityCode.Length is < 3 or > 4 ||
            card.SecurityCode.Any(x => !char.IsAsciiDigit(x)))
            throw CommerceException.BadRequest("INVALID_CARD", "Card details are incomplete or invalid.");
        if (card.BillingAddress is null || string.IsNullOrWhiteSpace(card.BillingAddress.AddressLine1) ||
            string.IsNullOrWhiteSpace(card.BillingAddress.City) ||
            string.IsNullOrWhiteSpace(card.BillingAddress.PostalCode) ||
            card.BillingAddress.CountryCode?.Length != 2)
            throw CommerceException.BadRequest("INVALID_BILLING_ADDRESS", "A complete billing address is required.");
    }

    private static void ValidateAddress(ShippingAddressRequest address)
    {
        if (string.IsNullOrWhiteSpace(address.Street) || string.IsNullOrWhiteSpace(address.City) ||
            string.IsNullOrWhiteSpace(address.Country) || string.IsNullOrWhiteSpace(address.ZipCode))
            throw CommerceException.BadRequest("INVALID_SHIPPING_ADDRESS",
                "Street, city, country and zipCode are required.");
    }

    private static void EnsureMoney(decimal expectedAmount, string expectedCurrency,
        decimal actualAmount, string actualCurrency, string operation)
    {
        if (decimal.Round(expectedAmount, 2) != decimal.Round(actualAmount, 2) ||
            !string.Equals(expectedCurrency, actualCurrency, StringComparison.OrdinalIgnoreCase))
            throw CommerceException.Conflict("PAYPAL_AMOUNT_MISMATCH",
                $"PayPal's {operation} amount or currency did not match the order.");
    }

    private static OrderResponse Map(Order order)
    {
        var captured = order.CapturedAmount;
        return new OrderResponse(order.Id, order.OrderDate, order.Total(),
            order.OrderItems.Select(x => new OrderItemResponse(x.ItemOrdered.CatalogItemId,
                x.ItemOrdered.ProductName, x.UnitPrice, x.Units)).ToArray(),
            new OrderPaymentResponse(order.PaymentStatus.ToString(), order.Currency,
                order.PayPalOrderId, order.PayPalOrderStatus, order.AuthorizationId,
                order.AuthorizationStatus, order.AuthorizedAmount, order.AuthorizationExpiresAt,
                order.CaptureId, order.CaptureStatus, captured, order.PayPalFee, order.NetProceeds,
                order.RefundedAmount, captured is null ? null : captured - order.RefundedAmount,
                order.Refunds.Select(x => new RefundResponse(x.Id, x.PayPalRefundId, x.Status,
                    x.Amount, x.CreatedAt)).ToArray()));
    }

    private static RefundOrderResponse MapRefund(Order order, PaymentRefund refund) => new(
        refund.Id, refund.PayPalRefundId, refund.Status, refund.Amount, order.Currency!,
        order.CapturedAmount!.Value - order.RefundedAmount);

    private static Dictionary<string, ReconciliationResource> BuildResourceLookup(IEnumerable<Order> orders)
    {
        var result = new Dictionary<string, ReconciliationResource>(StringComparer.OrdinalIgnoreCase);
        foreach (var order in orders)
        {
            if (order.AuthorizationId is not null && order.AuthorizationCreatedAt is not null)
                result[order.AuthorizationId] = new(order.Id, order.PaymentStatus.ToString(),
                    "Authorization", order.AuthorizationCreatedAt.Value,
                    order.AuthorizedAmount ?? order.Total(), order.Currency!);
            if (order.CaptureId is not null && order.CapturedAt is not null)
                result[order.CaptureId] = new(order.Id, order.PaymentStatus.ToString(), "Capture",
                    order.CapturedAt.Value, order.CapturedAmount ?? order.Total(), order.Currency!);
            foreach (var refund in order.Refunds)
                result[refund.PayPalRefundId] = new(order.Id, order.PaymentStatus.ToString(), "Refund",
                    refund.CreatedAt, refund.Amount, order.Currency!);
        }
        return result;
    }

    private static string RequestId(string value)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(value));
        return "eshop-" + Convert.ToHexString(hash).ToLowerInvariant();
    }

    private static string CustomerId(string ownerId)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(ownerId));
        return "eshop-" + Convert.ToHexString(hash)[..32].ToLowerInvariant();
    }

    private static async Task<AsyncLockReleaser> AcquireAsync(string key,
        CancellationToken cancellationToken)
    {
        var semaphore = OperationLocks.GetOrAdd(key, _ => new SemaphoreSlim(1, 1));
        await semaphore.WaitAsync(cancellationToken);
        return new AsyncLockReleaser(semaphore);
    }

    private sealed class AsyncLockReleaser : IAsyncDisposable
    {
        private readonly SemaphoreSlim _semaphore;
        public AsyncLockReleaser(SemaphoreSlim semaphore) => _semaphore = semaphore;
        public ValueTask DisposeAsync()
        {
            _semaphore.Release();
            return ValueTask.CompletedTask;
        }
    }

    private sealed record ReconciliationResource(int OrderId, string PaymentState,
        string ResourceType, DateTimeOffset OccurredAt, decimal Amount, string Currency);
}
