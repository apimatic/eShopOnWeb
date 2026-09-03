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
using Microsoft.eShopWeb.ApplicationCore.Entities.BuyerAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.Infrastructure.Data;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.PublicApi.Payments;

public sealed class PaymentApplicationService
{
    private static readonly ConcurrentDictionary<string, SemaphoreSlim> Locks = new();
    private readonly CatalogContext _db;
    private readonly IPayPalGateway _payPal;
    private readonly PayPalSettings _settings;
    private readonly ILogger<PaymentApplicationService> _logger;

    public PaymentApplicationService(CatalogContext db, IPayPalGateway payPal,
        IOptions<PayPalSettings> settings, ILogger<PaymentApplicationService> logger)
    {
        _db = db;
        _payPal = payPal;
        _settings = settings.Value;
        _logger = logger;
    }

    public async Task<CreateOrderResponse> CreateOrderAsync(string buyerId, CreateOrderRequest request,
        CancellationToken cancellationToken)
    {
        EnsureBuyerId(buyerId);
        if (request.Items is null || request.Items.Count == 0)
            throw new PaymentDomainException(400, "At least one catalog item is required.");
        if (request.ShippingAddress is null)
            throw new PaymentDomainException(400, "A shipping address is required.");

        ValidateAddress(request.ShippingAddress, requireTwoLetterCountry: false);
        if (request.Items.Any(x => x.CatalogItemId <= 0 || x.Quantity is <= 0 or > 1000))
            throw new PaymentDomainException(400, "Item quantities must be between 1 and 1000.");
        var grouped = request.Items.GroupBy(x => x.CatalogItemId)
            .ToDictionary(x => x.Key, x => x.Sum(y => (long)y.Quantity));
        if (grouped.Count > 100 || grouped.Values.Any(x => x > 1000))
            throw new PaymentDomainException(400, "An order can contain at most 100 items and 1000 units per item.");
        var requested = grouped.ToDictionary(x => x.Key, x => (int)x.Value);

        var catalogItems = await _db.CatalogItems
            .Where(x => requested.Keys.Contains(x.Id))
            .ToListAsync(cancellationToken);
        if (catalogItems.Count != requested.Count)
            throw new PaymentDomainException(400, "One or more catalog items do not exist.");

        var lines = catalogItems.Select(item => new OrderItem(
            new CatalogItemOrdered(item.Id, item.Name, item.PictureUri),
            item.Price,
            requested[item.Id])).ToList();
        var address = request.ShippingAddress;
        var order = new Order(buyerId,
            new Address(address.Street.Trim(), address.City.Trim(), address.State?.Trim() ?? string.Empty,
                address.Country.Trim(), address.ZipCode.Trim()),
            lines,
            _settings.Currency);
        _db.Orders.Add(order);
        await _db.SaveChangesAsync(cancellationToken);
        return new CreateOrderResponse(order.Id, order.Total(), _settings.Currency,
            order.Payment!.Status.ToString());
    }

    public async Task<PaymentResponse> PayAsync(string buyerId, int orderId, PayOrderRequest request,
        CancellationToken cancellationToken)
    {
        EnsureBuyerId(buyerId);
        if ((request.Card is null) == (request.PaymentMethodId is null))
            throw new PaymentDomainException(400, "Supply either card details or paymentMethodId, but not both.");

        return await Locked($"order:{orderId}", async () =>
        {
            var order = await LoadOrder(orderId, cancellationToken);
            EnsureOwner(order, buyerId);
            var payment = RequirePayment(order);
            if (payment.Status is OrderPaymentStatus.Authorized or OrderPaymentStatus.Captured or
                OrderPaymentStatus.RefundPending or OrderPaymentStatus.PartiallyRefunded or
                OrderPaymentStatus.Refunded)
                return ToPayment(order);
            if (order.FulfillmentStatus != OrderFulfillmentStatus.Pending)
                throw new PaymentDomainException(409, "Only a pending order can be paid.");

            object source;
            int? methodId = null;
            if (request.PaymentMethodId is int requestedMethodId)
            {
                var method = await _db.PaymentMethods.SingleOrDefaultAsync(
                    x => x.Id == requestedMethodId && x.BuyerId == buyerId && x.State == PaymentMethodState.Active,
                    cancellationToken);
                if (method?.ProviderTokenId is null)
                    throw new PaymentDomainException(404, "Saved payment method was not found.");
                source = new SavedCardSource(method.ProviderTokenId);
                methodId = method.Id;
            }
            else
            {
                ValidateCard(request.Card!);
                source = new CardSource(request.Card!);
            }

            try
            {
                var result = await _payPal.AuthorizeAsync(order.Id, payment.PaymentReference, order.Total(),
                    payment.Currency, source, cancellationToken);
                payment.BeginAuthorization(result.ProviderOrderId, methodId);
                payment.RecordAuthorization(result.AuthorizationId, result.Status, result.Amount, result.ExpiresAt);
                await _db.SaveChangesAsync(cancellationToken);
                _logger.LogInformation("Authorized eShop order {OrderId} as PayPal authorization {AuthorizationId} with status {Status}.",
                    order.Id, result.AuthorizationId, result.Status);
                return ToPayment(order);
            }
            catch (PayPalPayerActionRequiredException ex)
            {
                payment.RecordPayerActionRequired(ex.ProviderOrderId);
                await _db.SaveChangesAsync(cancellationToken);
                throw;
            }
        });
    }

    public async Task<PaymentResponse> FulfilAsync(int orderId, CancellationToken cancellationToken)
    {
        return await Locked($"order:{orderId}", async () =>
        {
            var order = await LoadOrder(orderId, cancellationToken);
            var payment = RequirePayment(order);
            if (order.FulfillmentStatus == OrderFulfillmentStatus.Fulfilled &&
                payment.Status is OrderPaymentStatus.Captured or OrderPaymentStatus.RefundPending or
                    OrderPaymentStatus.PartiallyRefunded or OrderPaymentStatus.Refunded)
                return ToPayment(order);
            if (order.FulfillmentStatus == OrderFulfillmentStatus.Cancelled)
                throw new PaymentDomainException(409, "A cancelled order cannot be fulfilled.");
            if (payment.AuthorizationId is null)
                throw new PaymentDomainException(409, "The order has not been authorized for payment.");

            var providerAuthorization = await _payPal.GetAuthorizationAsync(payment.AuthorizationId, cancellationToken);
            if (providerAuthorization.Amount != order.Total() ||
                !string.Equals(providerAuthorization.Currency, payment.Currency, StringComparison.Ordinal))
                throw new PaymentDomainException(409,
                    "PayPal's authorization amount or currency no longer matches this order; ask the shopper to authorize a new payment.");
            if (providerAuthorization.Status is "DENIED" or "VOIDED")
                throw new PaymentDomainException(409,
                    $"PayPal authorization is {providerAuthorization.Status}; ask the shopper to authorize another payment.");
            if (providerAuthorization.Status == "PENDING")
                throw new PaymentDomainException(409,
                    "PayPal authorization is still pending; retry fulfilment after it reaches CREATED.");

            var now = DateTimeOffset.UtcNow;
            var outsideRenewalWindow = payment.AuthorizedAt is not null && payment.AuthorizedAt <= now.AddDays(-29);
            if (outsideRenewalWindow)
                throw new PaymentDomainException(409,
                    "The PayPal authorization is older than 29 days and cannot be renewed; ask the shopper to authorize a new payment.");

            var stale = providerAuthorization.ExpiresAt <= now.AddMinutes(5) ||
                        payment.AuthorizedAt is not null && payment.AuthorizedAt <= now.AddDays(-3);
            if (stale)
            {
                ReauthorizationResult renewed;
                try
                {
                    renewed = await _payPal.ReauthorizeAsync(order.Id, payment.PaymentReference,
                        payment.AuthorizationId, order.Total(), payment.Currency, cancellationToken);
                }
                catch (PayPalProviderException ex)
                {
                    _logger.LogWarning(ex,
                        "PayPal authorization {AuthorizationId} for order {OrderId} could not be renewed; debug id {PayPalDebugId}.",
                        payment.AuthorizationId, order.Id, ex.DebugId);
                    throw new PaymentDomainException(409,
                        $"PayPal could not renew the stale authorization; ask the shopper to authorize a new payment. {ex.Message}");
                }
                payment.RecordReauthorization(renewed.AuthorizationId, renewed.Status, renewed.Amount, renewed.ExpiresAt);
                await _db.SaveChangesAsync(cancellationToken);
            }

            payment.BeginCapture();
            await _db.SaveChangesAsync(cancellationToken);
            var capture = await _payPal.CaptureAsync(order.Id, payment.PaymentReference,
                payment.AuthorizationId!, order.Total(), payment.Currency, cancellationToken);
            payment.RecordCapture(capture.CaptureId, capture.Status, capture.Amount, capture.Fee, capture.Net);
            if (capture.Status != "COMPLETED")
            {
                await _db.SaveChangesAsync(cancellationToken);
                throw new PaymentDomainException(409,
                    $"PayPal capture is {capture.Status}; do not ship until PayPal reports COMPLETED.");
            }

            order.MarkFulfilled();
            await _db.SaveChangesAsync(cancellationToken);
            _logger.LogInformation("Captured PayPal payment {CaptureId} and fulfilled eShop order {OrderId}.",
                capture.CaptureId, order.Id);
            return ToPayment(order);
        });
    }

    public async Task<PaymentResponse> CancelAsync(int orderId, CancellationToken cancellationToken)
    {
        return await Locked($"order:{orderId}", async () =>
        {
            var order = await LoadOrder(orderId, cancellationToken);
            var payment = RequirePayment(order);
            if (order.FulfillmentStatus == OrderFulfillmentStatus.Cancelled) return ToPayment(order);
            if (order.FulfillmentStatus == OrderFulfillmentStatus.Fulfilled ||
                payment.Status is OrderPaymentStatus.Captured or OrderPaymentStatus.RefundPending or
                    OrderPaymentStatus.PartiallyRefunded or OrderPaymentStatus.Refunded)
                throw new PaymentDomainException(409, "A fulfilled or captured order must be refunded, not cancelled.");

            if (payment.AuthorizationId is not null && payment.Status != OrderPaymentStatus.Voided)
            {
                var result = await _payPal.VoidAsync(order.Id, payment.PaymentReference,
                    payment.AuthorizationId, cancellationToken);
                payment.RecordVoid(result.Status);
            }
            order.MarkCancelled();
            await _db.SaveChangesAsync(cancellationToken);
            return ToPayment(order);
        });
    }

    public async Task<RefundResponse> RefundAsync(string buyerId, int orderId, RefundOrderRequest request,
        CancellationToken cancellationToken)
    {
        EnsureBuyerId(buyerId);
        if (string.IsNullOrWhiteSpace(request.IdempotencyKey) || request.IdempotencyKey.Trim().Length > 200)
            throw new PaymentDomainException(400, "idempotencyKey must contain between 1 and 200 characters.");

        return await Locked($"order:{orderId}", async () =>
        {
            var order = await LoadOrder(orderId, cancellationToken);
            EnsureOwner(order, buyerId);
            var payment = RequirePayment(order);
            if (order.FulfillmentStatus != OrderFulfillmentStatus.Fulfilled || payment.CaptureId is null ||
                payment.CapturedAmount is null)
                throw new PaymentDomainException(409, "Only a captured, fulfilled order can be refunded.");

            var key = request.IdempotencyKey.Trim();
            var existing = payment.Refunds.SingleOrDefault(x => x.IdempotencyKey == key);
            if (existing?.ProviderRefundId is not null)
            {
                if (existing.Status == "PENDING")
                {
                    var refreshed = await _payPal.GetRefundAsync(existing.ProviderRefundId, existing.Amount,
                        payment.Currency, cancellationToken);
                    payment.RecordRefund(existing, refreshed.RefundId, refreshed.Status, refreshed.Amount);
                    await _db.SaveChangesAsync(cancellationToken);
                }
                return ToRefund(existing);
            }

            var remaining = payment.CapturedAmount.Value - payment.ReservedRefundAmount;
            var amount = existing?.Amount ?? request.Amount ?? remaining;
            if (amount <= 0m || amount > remaining && existing is null)
                throw new PaymentDomainException(409, "Refund amount exceeds the captured amount remaining.");

            var providerRequestId = existing?.ProviderRequestId ?? RefundRequestId(payment.PaymentReference, key);
            var refund = existing ?? payment.BeginRefund(key, providerRequestId, amount);
            if (existing is null) await _db.SaveChangesAsync(cancellationToken);

            var result = await _payPal.RefundAsync(order.Id, payment.PaymentReference, payment.CaptureId,
                refund.Amount, payment.Currency, providerRequestId, cancellationToken);
            payment.RecordRefund(refund, result.RefundId, result.Status, result.Amount);
            await _db.SaveChangesAsync(cancellationToken);
            _logger.LogInformation("Refunded eShop order {OrderId} with PayPal refund {RefundId}, status {Status}.",
                order.Id, result.RefundId, result.Status);
            return ToRefund(refund);
        });
    }

    public async Task<PaymentMethodResponse> SavePaymentMethodAsync(string buyerId,
        SavePaymentMethodRequest request, CancellationToken cancellationToken)
    {
        EnsureBuyerId(buyerId);
        ValidateCard(request.Card);
        var method = new PaymentMethod(buyerId);
        _db.PaymentMethods.Add(method);
        await _db.SaveChangesAsync(cancellationToken);

        return await Locked($"method:{method.Id}", async () =>
        {
            var result = await _payPal.SaveCardAsync(method.PaymentReference, buyerId, request.Card,
                cancellationToken);
            method.Activate(result.TokenId, result.CustomerId, result.Brand, result.Last4, result.Expiry);
            await _db.SaveChangesAsync(cancellationToken);
            _logger.LogInformation("Saved PayPal payment method {PaymentMethodId} for shopper {BuyerId}.",
                method.Id, buyerId);
            return ToMethod(method);
        });
    }

    public async Task<IReadOnlyList<PaymentMethodResponse>> GetPaymentMethodsAsync(string buyerId,
        CancellationToken cancellationToken)
    {
        EnsureBuyerId(buyerId);
        return await _db.PaymentMethods
            .Where(x => x.BuyerId == buyerId && x.State == PaymentMethodState.Active)
            .OrderBy(x => x.Id)
            .Select(x => new PaymentMethodResponse(x.Id, x.Brand!, x.Last4!, x.Expiry))
            .ToListAsync(cancellationToken);
    }

    public async Task DeletePaymentMethodAsync(string buyerId, int paymentMethodId,
        CancellationToken cancellationToken)
    {
        EnsureBuyerId(buyerId);
        await Locked($"method:{paymentMethodId}", async () =>
        {
            var method = await _db.PaymentMethods.SingleOrDefaultAsync(
                x => x.Id == paymentMethodId && x.BuyerId == buyerId &&
                     (x.State == PaymentMethodState.Active ||
                      x.State == PaymentMethodState.PendingProviderDeletion),
                cancellationToken);
            if (method is null) throw new PaymentDomainException(404, "Saved payment method was not found.");

            if (method.State == PaymentMethodState.Active)
            {
                method.MarkDeleted(providerCleanupPending: true);
                await _db.SaveChangesAsync(cancellationToken);
            }
            try
            {
                if (method.ProviderTokenId is not null)
                    await _payPal.DeleteCardAsync(method.ProviderTokenId, cancellationToken);
                method.MarkDeleted(providerCleanupPending: false);
                await _db.SaveChangesAsync(cancellationToken);
            }
            catch (PayPalProviderException ex) when (ex.Code is "RESOURCE_NOT_FOUND" or "404")
            {
                method.MarkDeleted(providerCleanupPending: false);
                await _db.SaveChangesAsync(cancellationToken);
            }
            catch
            {
                await _db.SaveChangesAsync(cancellationToken);
                throw;
            }
            return true;
        });
    }

    public async Task<IReadOnlyList<OrderResponse>> GetMyOrdersAsync(string buyerId,
        CancellationToken cancellationToken)
    {
        EnsureBuyerId(buyerId);
        var orders = await OrderQuery().Where(x => x.BuyerId == buyerId)
            .OrderByDescending(x => x.OrderDate)
            .ToListAsync(cancellationToken);
        return orders.Select(ToOrder).ToList();
    }

    public async Task<ReconciliationResponse> ReconcileAsync(DateTimeOffset from, DateTimeOffset to,
        CancellationToken cancellationToken)
    {
        if (from >= to) throw new PaymentDomainException(400, "from must be earlier than to.");
        if (to - from > TimeSpan.FromDays(1095))
            throw new PaymentDomainException(400, "The reconciliation range cannot exceed three years.");

        var provider = await _payPal.SearchTransactionsAsync(from, to, _settings.Currency, cancellationToken);
        var providerIds = provider.Select(x => x.TransactionId).Where(x => x is not null).Cast<string>().ToList();
        var providerReferences = provider.Select(x => ParsePaymentReference(x.InvoiceId))
            .Where(x => x is not null).Cast<string>().ToList();
        var localOrders = await OrderQuery()
            .Where(x => x.Payment != null && x.Payment.ProviderOrderId != null &&
                        (x.Payment.AuthorizedAt >= from && x.Payment.AuthorizedAt <= to ||
                         x.Payment.CapturedAt >= from && x.Payment.CapturedAt <= to ||
                         x.Payment.Refunds.Any(r => r.UpdatedAt >= from && r.UpdatedAt <= to) ||
                         providerReferences.Contains(x.Payment.PaymentReference) ||
                         providerIds.Contains(x.Payment.AuthorizationId!) ||
                         providerIds.Contains(x.Payment.CaptureId!) ||
                         x.Payment.Refunds.Any(r => providerIds.Contains(r.ProviderRefundId!))))
            .ToListAsync(cancellationToken);
        var localById = localOrders.ToDictionary(x => x.Id);
        var localExpected = localOrders.Where(x =>
            x.Payment!.AuthorizedAt >= from && x.Payment.AuthorizedAt <= to ||
            x.Payment.CapturedAt >= from && x.Payment.CapturedAt <= to ||
            x.Payment.Refunds.Any(r => r.UpdatedAt >= from && r.UpdatedAt <= to)).ToList();
        var matched = new HashSet<int>();

        var rows = provider.Select(transaction =>
        {
            var paymentReference = ParsePaymentReference(transaction.InvoiceId);
            int? orderId = localOrders.FirstOrDefault(x =>
                x.Payment?.PaymentReference == paymentReference)?.Id;
            if (orderId is null)
            {
                orderId = localOrders.FirstOrDefault(x =>
                    x.Payment?.CaptureId == transaction.TransactionId ||
                    x.Payment?.AuthorizationId == transaction.TransactionId ||
                    x.Payment?.Refunds.Any(r => r.ProviderRefundId == transaction.TransactionId) == true)?.Id;
            }
            if (orderId is int id && localById.ContainsKey(id)) matched.Add(id);
            var state = orderId is null || !localById.ContainsKey(orderId.Value)
                ? "PayPalOnly"
                : "Matched";
            return new ReconciliationTransaction(transaction.TransactionId, transaction.ReferenceId,
                transaction.EventCode, transaction.InitiatedAt, transaction.Amount, transaction.Fee,
                transaction.Currency, transaction.Status, transaction.InvoiceId, orderId, state);
        }).ToList();

        var missing = localExpected.Where(x => !matched.Contains(x.Id)).Select(x => x.Id).OrderBy(x => x).ToList();
        return new ReconciliationResponse(from, to, rows, missing);
    }

    private IQueryable<Order> OrderQuery() => _db.Orders
        .Include(x => x.OrderItems).ThenInclude(x => x.ItemOrdered)
        .Include(x => x.Payment).ThenInclude(x => x!.Refunds);

    private async Task<Order> LoadOrder(int orderId, CancellationToken cancellationToken) =>
        await OrderQuery().SingleOrDefaultAsync(x => x.Id == orderId, cancellationToken) ??
        throw new PaymentDomainException(404, "Order was not found.");

    private static void EnsureOwner(Order order, string buyerId)
    {
        if (!string.Equals(order.BuyerId, buyerId, StringComparison.Ordinal))
            throw new PaymentDomainException(404, "Order was not found.");
    }

    private static OrderPayment RequirePayment(Order order) => order.Payment ??
        throw new PaymentDomainException(409, "This legacy order does not have a PayPal payment.");

    private static void ValidateCard(CardRequest? card)
    {
        if (card is null || card.BillingAddress is null ||
            card.Number.Length is < 13 or > 19 || card.Number.Any(c => !char.IsDigit(c)) ||
            card.SecurityCode.Length is < 3 or > 4 || card.SecurityCode.Any(c => !char.IsDigit(c)) ||
            !DateTime.TryParseExact(card.Expiry + "-01", "yyyy-MM-dd", CultureInfo.InvariantCulture,
                DateTimeStyles.None, out var expiry) || expiry < DateTime.UtcNow.Date.AddDays(1 - DateTime.UtcNow.Day) ||
            string.IsNullOrWhiteSpace(card.Name))
            throw new PaymentDomainException(400, "Card details are invalid or expired.");

        ValidateAddress(card.BillingAddress, requireTwoLetterCountry: true);
    }

    private static void ValidateAddress(PostalAddressRequest? address, bool requireTwoLetterCountry)
    {
        if (address is null || string.IsNullOrWhiteSpace(address.Street) || string.IsNullOrWhiteSpace(address.City) ||
            string.IsNullOrWhiteSpace(address.Country) || string.IsNullOrWhiteSpace(address.ZipCode) ||
            requireTwoLetterCountry && address.Country.Trim().Length != 2)
        {
            throw new PaymentDomainException(400, "Address details are incomplete or invalid.");
        }
    }

    private static void EnsureBuyerId(string buyerId)
    {
        if (string.IsNullOrWhiteSpace(buyerId))
            throw new PaymentDomainException(401, "Authenticated user identity is missing.");
    }

    private static PaymentResponse ToPayment(Order order)
    {
        var p = RequirePayment(order);
        return new PaymentResponse(order.Id, p.Status.ToString(), p.AuthorizationId, p.AuthorizedAmount,
            p.AuthorizationExpiresAt, p.CaptureId, p.CapturedAmount, p.PayPalFee, p.NetAmount, p.Currency);
    }

    private static RefundResponse ToRefund(PaymentRefund refund) =>
        new(refund.Id, refund.ProviderRefundId, refund.Amount, refund.Status);

    private static PaymentMethodResponse ToMethod(PaymentMethod method) =>
        new(method.Id, method.Brand!, method.Last4!, method.Expiry);

    private static OrderResponse ToOrder(Order order)
    {
        var lines = order.OrderItems.Select(x => new OrderLineResponse(x.ItemOrdered.CatalogItemId,
            x.ItemOrdered.ProductName, x.UnitPrice, x.Units)).ToList();
        var payment = order.Payment is null ? null : ToPayment(order);
        var refunds = order.Payment?.Refunds.Select(ToRefund).ToList() ?? [];
        return new OrderResponse(order.Id, order.OrderDate, order.Total(), order.Payment?.Currency ?? string.Empty,
            order.Payment?.Status.ToString() ?? OrderPaymentStatus.NotRequired.ToString(),
            order.FulfillmentStatus.ToString(), payment, lines, refunds);
    }

    private static string RefundRequestId(string paymentReference, string key)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes($"{paymentReference}:{key}"));
        return $"eshop-refund-{Convert.ToHexString(bytes)[..20].ToLowerInvariant()}";
    }

    private static string? ParsePaymentReference(string? invoiceId)
    {
        const string prefix = "eshop-";
        var value = invoiceId?.StartsWith(prefix, StringComparison.Ordinal) == true
            ? invoiceId[prefix.Length..]
            : null;
        return value?.Length == 32 ? value : null;
    }

    private static async Task<T> Locked<T>(string key, Func<Task<T>> action)
    {
        var gate = Locks.GetOrAdd(key, _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync();
        try { return await action(); }
        finally { gate.Release(); }
    }
}
