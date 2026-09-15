using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
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
    private readonly IPayPalGateway _gateway;
    private readonly PaymentOperationLock _operationLock;
    private readonly PayPalOptions _options;

    public PaymentApplicationService(CatalogContext db, IPayPalGateway gateway,
        PaymentOperationLock operationLock, IOptions<PayPalOptions> options)
    {
        _db = db;
        _gateway = gateway;
        _operationLock = operationLock;
        _options = options.Value;
    }

    public async Task<OrderPaymentDto> PlaceOrderAsync(string buyerId, PlaceOrderRequest request,
        CancellationToken cancellationToken)
    {
        EnsureConfiguration();
        if (request.Items is null || request.Items.Count == 0)
            throw new PaymentApiException(400, "At least one catalog item is required.");
        if (request.Items.Any(x => x.CatalogItemId <= 0 || x.Quantity <= 0 || x.Quantity > 100))
            throw new PaymentApiException(400, "Catalog item ids and quantities must be valid; quantity cannot exceed 100.");

        var requested = request.Items.GroupBy(x => x.CatalogItemId)
            .ToDictionary(x => x.Key, x => x.Sum(y => y.Quantity));
        var catalogItems = await _db.CatalogItems.Where(x => requested.Keys.Contains(x.Id))
            .ToListAsync(cancellationToken);
        if (catalogItems.Count != requested.Count)
            throw new PaymentApiException(400, "One or more catalog items do not exist.");

        var lines = catalogItems.Select(item => new OrderItem(
            new CatalogItemOrdered(item.Id, item.Name, item.PictureUri), item.Price, requested[item.Id])).ToList();
        var address = request.ShippingAddress is null
            ? new Address("Not supplied", "Not supplied", string.Empty, "Not supplied", "Not supplied")
            : new Address(request.ShippingAddress.AddressLine1, request.ShippingAddress.City,
                request.ShippingAddress.State, request.ShippingAddress.CountryCode,
                request.ShippingAddress.PostalCode);
        var order = new Order(buyerId, address, lines, Currency());
        EnsureCentAmount(order.Total());
        _db.Orders.Add(order);
        await _db.SaveChangesAsync(cancellationToken);
        return Dto(order);
    }

    public async Task<OrderPaymentDto> PayAsync(int orderId, string buyerId, PayOrderRequest request,
        CancellationToken cancellationToken)
    {
        EnsureConfiguration();
        using var held = await _operationLock.AcquireAsync($"order:{orderId}", cancellationToken);
        var order = await OwnedOrder(orderId, buyerId, cancellationToken);
        if (order.PaymentState == OrderPaymentState.Authorized || order.PaymentState == OrderPaymentState.AuthorizationPending &&
            order.PayPalAuthorizationId is not null)
            return Dto(order);
        if (order.PaymentState != OrderPaymentState.AwaitingPayment && order.PaymentState != OrderPaymentState.AuthorizationPending)
            throw new PaymentApiException(409, "This order is not awaiting payment.");
        if ((request.Card is null) == (request.PaymentMethodId is null))
            throw new PaymentApiException(400, "Provide either card details or one saved paymentMethodId, but not both.");
        if (request.Card is not null) ValidateCard(request.Card);

        string? vaultId = null;
        if (request.PaymentMethodId is int methodId)
        {
            var method = await _db.PaymentMethods.FirstOrDefaultAsync(x => x.Id == methodId &&
                x.BuyerId == buyerId && !x.IsDeleted && x.Status == "ACTIVE", cancellationToken);
            if (method?.PayPalPaymentTokenId is null)
                throw new PaymentApiException(404, "The saved payment method was not found.");
            vaultId = method.PayPalPaymentTokenId;
        }

        var total = order.Total();
        EnsureCentAmount(total);
        order.PreparePayment(Currency(), $"eshop-create-{Guid.NewGuid():N}",
            $"eshop-authorize-{Guid.NewGuid():N}");
        await _db.SaveChangesAsync(cancellationToken);

        if (order.PayPalOrderId is null)
        {
            var providerOrderId = await _gateway.CreateOrderAsync(order.Id, total, order.Currency,
                order.PayPalCreateRequestId!, cancellationToken);
            order.RecordPayPalOrder(providerOrderId);
            await _db.SaveChangesAsync(cancellationToken);
        }

        ProviderAuthorization authorization;
        try
        {
            authorization = await _gateway.AuthorizeOrderAsync(order.PayPalOrderId!, total,
                request.Card, vaultId, order.PayPalAuthorizeRequestId!, cancellationToken);
        }
        catch (PayPalProviderException)
        {
            var recovered = await _gateway.GetOrderAuthorizationAsync(order.PayPalOrderId!, cancellationToken);
            if (recovered is null) throw;
            authorization = recovered;
        }
        EnsureProviderAmount(total, authorization.Amount, "authorized");
        order.RecordAuthorization(authorization.AuthorizationId, authorization.Status,
            authorization.Amount, authorization.Expiration);
        await _db.SaveChangesAsync(cancellationToken);
        return Dto(order);
    }

    public async Task<OrderPaymentDto> FulfilAsync(int orderId, CancellationToken cancellationToken)
    {
        EnsureConfiguration();
        using var held = await _operationLock.AcquireAsync($"order:{orderId}", cancellationToken);
        var order = await AnyOrder(orderId, cancellationToken);
        if (order.PaymentState is OrderPaymentState.Fulfilled or OrderPaymentState.PartiallyRefunded or OrderPaymentState.Refunded)
            return Dto(order);
        if (order.PaymentState == OrderPaymentState.Cancelled)
            throw new PaymentApiException(409, "A cancelled order cannot be fulfilled.");
        if (order.PayPalAuthorizationId is null || order.AuthorizedAmount is null)
            throw new PaymentApiException(409, "The order must be paid and authorized before fulfilment.");

        if (order.PayPalCaptureId is not null)
        {
            var existingCapture = await _gateway.GetCaptureAsync(order.PayPalCaptureId, cancellationToken);
            ApplyCapture(order, existingCapture);
            await _db.SaveChangesAsync(cancellationToken);
            return Dto(order);
        }

        var authorization = await _gateway.GetAuthorizationAsync(order.PayPalAuthorizationId, cancellationToken);
        EnsureProviderAmount(order.Total(), authorization.Amount, "authorized");
        if (authorization.Expiration is not null && authorization.Expiration <= DateTimeOffset.UtcNow)
            throw new PaymentApiException(409,
                "The PayPal authorization has expired and cannot be renewed; ask the shopper to authorize payment again.");

        if (authorization.Expiration is not null && authorization.Expiration <= DateTimeOffset.UtcNow.AddDays(26))
        {
            try
            {
                var requestId = order.PrepareReauthorization($"eshop-reauthorize-{Guid.NewGuid():N}");
                await _db.SaveChangesAsync(cancellationToken);
                authorization = await _gateway.ReauthorizeAsync(authorization.AuthorizationId,
                    authorization.Amount, order.Currency, requestId, cancellationToken);
                order.RecordReauthorization(authorization.AuthorizationId, authorization.Status,
                    authorization.Amount, authorization.Expiration);
                await _db.SaveChangesAsync(cancellationToken);
            }
            catch (PayPalProviderException ex)
            {
                throw new PaymentApiException(409,
                    "PayPal can no longer renew this authorization; ask the shopper to authorize payment again.", ex);
            }
        }

        order.PrepareCapture($"eshop-capture-{Guid.NewGuid():N}");
        await _db.SaveChangesAsync(cancellationToken);
        var capture = await _gateway.CaptureAsync(authorization.AuthorizationId, order.Total(), order.Currency,
            order.PayPalCaptureRequestId!, cancellationToken);
        if (capture.Status == "COMPLETED" && (capture.Fee is null || capture.Net is null))
            capture = await _gateway.GetCaptureAsync(capture.CaptureId, cancellationToken);
        ApplyCapture(order, capture);
        await _db.SaveChangesAsync(cancellationToken);
        return Dto(order);
    }

    public async Task<OrderPaymentDto> CancelAsync(int orderId, CancellationToken cancellationToken)
    {
        using var held = await _operationLock.AcquireAsync($"order:{orderId}", cancellationToken);
        var order = await AnyOrder(orderId, cancellationToken);
        if (order.PaymentState == OrderPaymentState.Cancelled) return Dto(order);
        if (order.PaymentState is OrderPaymentState.Fulfilled or OrderPaymentState.PartiallyRefunded or OrderPaymentState.Refunded or OrderPaymentState.CapturePending)
            throw new PaymentApiException(409, "An order with captured or capturing funds cannot be cancelled; refund it instead.");

        if (order.PayPalAuthorizationId is null && order.PayPalOrderId is not null)
        {
            var found = await _gateway.GetOrderAuthorizationAsync(order.PayPalOrderId, cancellationToken);
            if (found is not null)
                order.RecordAuthorization(found.AuthorizationId, found.Status, found.Amount, found.Expiration);
        }
        if (order.PayPalAuthorizationId is null)
        {
            order.CancelWithoutAuthorization();
        }
        else
        {
            order.PrepareVoid($"eshop-void-{Guid.NewGuid():N}");
            await _db.SaveChangesAsync(cancellationToken);
            var result = await _gateway.VoidAsync(order.PayPalAuthorizationId,
                order.PayPalVoidRequestId!, cancellationToken);
            order.RecordVoid(result.Status);
            if (result.Status != "VOIDED")
                throw new PaymentApiException(409, "PayPal has not yet confirmed release of the held funds.");
        }
        await _db.SaveChangesAsync(cancellationToken);
        return Dto(order);
    }

    public async Task<RefundDto> RefundAsync(int orderId, string buyerId, CreateRefundRequest request,
        CancellationToken cancellationToken)
    {
        EnsureConfiguration();
        if (string.IsNullOrWhiteSpace(request.IdempotencyKey) || request.IdempotencyKey.Length > 108)
            throw new PaymentApiException(400, "IdempotencyKey is required and cannot exceed 108 characters.");
        using var held = await _operationLock.AcquireAsync($"refund:{orderId}", cancellationToken);
        var order = await OwnedOrder(orderId, buyerId, cancellationToken);
        if (order.PayPalCaptureId is null || order.CapturedAmount is null ||
            order.PaymentState is not (OrderPaymentState.Fulfilled or OrderPaymentState.PartiallyRefunded or OrderPaymentState.Refunded))
            throw new PaymentApiException(409, "Only a captured payment can be refunded.");

        var existing = await _db.PaymentRefunds.FirstOrDefaultAsync(x => x.CaptureId == order.PayPalCaptureId &&
            x.IdempotencyKey == request.IdempotencyKey, cancellationToken);
        if (existing is not null)
        {
            await RefreshOrRetryRefund(order, existing, cancellationToken);
            return RefundDto(existing);
        }

        var reserved = await _db.PaymentRefunds.Where(x => x.OrderId == order.Id &&
                (x.Status == "IN_FLIGHT" || x.Status == "PENDING" || x.Status == "COMPLETED"))
            .SumAsync(x => x.RequestedAmount, cancellationToken);
        var remaining = order.CapturedAmount.Value - reserved;
        var requested = request.Amount ?? remaining;
        EnsureCentAmount(requested);
        if (requested <= 0 || requested > remaining)
            throw new PaymentApiException(409, $"The maximum refundable amount is {remaining:0.00} {order.Currency}.");

        var refund = new PaymentRefund(order.Id, buyerId, order.PayPalCaptureId,
            request.IdempotencyKey, requested, order.Currency);
        _db.PaymentRefunds.Add(refund);
        await _db.SaveChangesAsync(cancellationToken);
        await RefreshOrRetryRefund(order, refund, cancellationToken);
        return RefundDto(refund);
    }

    public async Task<PaymentMethodDto> SavePaymentMethodAsync(string buyerId,
        SavePaymentMethodRequest request, CancellationToken cancellationToken)
    {
        EnsureConfiguration();
        ValidateCard(request.Card);
        var method = new PaymentMethod(buyerId, $"eshop-setup-{Guid.NewGuid():N}",
            $"eshop-token-{Guid.NewGuid():N}");
        _db.PaymentMethods.Add(method);
        await _db.SaveChangesAsync(cancellationToken);
        try
        {
            var saved = await _gateway.SaveCardAsync(request.Card, method.SetupRequestId,
                method.TokenRequestId, cancellationToken);
            method.RecordSetupToken(saved.SetupTokenId, "APPROVED");
            method.Activate(saved.PaymentTokenId, saved.CustomerId, saved.Brand, saved.LastDigits,
                saved.Expiry, saved.CardholderName, saved.Status);
            await _db.SaveChangesAsync(cancellationToken);
            return MethodDto(method);
        }
        catch
        {
            method.MarkFailed();
            await _db.SaveChangesAsync(CancellationToken.None);
            throw;
        }
    }

    public async Task<IReadOnlyList<PaymentMethodDto>> GetPaymentMethodsAsync(string buyerId,
        CancellationToken cancellationToken) => await _db.PaymentMethods.AsNoTracking()
        .Where(x => x.BuyerId == buyerId && !x.IsDeleted && x.Status == "ACTIVE")
        .OrderByDescending(x => x.CreatedAt).Select(x => new PaymentMethodDto(x.Id, x.Brand,
            x.LastDigits, x.Expiry, x.CardholderName)).ToListAsync(cancellationToken);

    public async Task DeletePaymentMethodAsync(int paymentMethodId, string buyerId,
        CancellationToken cancellationToken)
    {
        using var held = await _operationLock.AcquireAsync($"method:{paymentMethodId}", cancellationToken);
        var method = await _db.PaymentMethods.FirstOrDefaultAsync(x => x.Id == paymentMethodId &&
            x.BuyerId == buyerId, cancellationToken);
        if (method is null) throw new PaymentApiException(404, "The saved payment method was not found.");
        if (method.IsDeleted) return;
        var tokenId = method.PayPalPaymentTokenId;
        method.Delete();
        await _db.SaveChangesAsync(cancellationToken);
        if (tokenId is not null) await _gateway.DeletePaymentTokenAsync(tokenId, cancellationToken);
    }

    public async Task<IReadOnlyList<OrderPaymentDto>> GetMyOrdersAsync(string buyerId,
        CancellationToken cancellationToken)
    {
        var orders = await _db.Orders.AsNoTracking().Where(x => x.BuyerId == buyerId)
            .Include(x => x.OrderItems).OrderByDescending(x => x.OrderDate)
            .ToListAsync(cancellationToken);
        return orders.Select(Dto).ToList();
    }

    public async Task<ReconciliationResponse> ReconcileAsync(DateTimeOffset from, DateTimeOffset to,
        CancellationToken cancellationToken)
    {
        if (from >= to) throw new PaymentApiException(400, "The from date-time must be earlier than to.");
        var provider = await _gateway.SearchTransactionsAsync(from, to, cancellationToken);
        var orders = await _db.Orders.AsNoTracking().ToListAsync(cancellationToken);
        var refunds = await _db.PaymentRefunds.AsNoTracking().ToListAsync(cancellationToken);
        var idToOrder = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var order in orders)
        {
            Add(order.PayPalOrderId, order.Id); Add(order.PayPalAuthorizationId, order.Id);
            Add(order.PayPalCaptureId, order.Id);
        }
        foreach (var refund in refunds) Add(refund.PayPalRefundId, refund.OrderId);

        var records = new List<ReconciliationRecord>();
        var seenAppIds = new HashSet<string>(StringComparer.Ordinal);
        foreach (var tx in provider)
        {
            int? orderId = Match(tx.TransactionId) ?? Match(tx.ReferenceId);
            if (orderId is null && int.TryParse(tx.InvoiceId ?? tx.CustomField,
                    NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed) &&
                orders.Any(x => x.Id == parsed)) orderId = parsed;
            if (orderId is not null)
            {
                seenAppIds.Add(tx.TransactionId);
                if (tx.ReferenceId is not null) seenAppIds.Add(tx.ReferenceId);
            }
            records.Add(new ReconciliationRecord("PayPal", tx.TransactionId, orderId,
                orderId is null ? "ProviderOnly" : "Matched", tx.Status, tx.Amount,
                tx.Currency, tx.InitiatedAt));
        }

        foreach (var order in orders.Where(x => x.OrderDate >= from && x.OrderDate <= to))
        {
            AddApp(order.PayPalAuthorizationId, order, order.AuthorizedAmount, order.PayPalAuthorizationStatus, order.OrderDate);
            AddApp(order.PayPalCaptureId, order, order.CapturedAmount, order.PayPalCaptureStatus, order.FulfilledAt);
        }
        foreach (var refund in refunds.Where(x => x.CreatedAt >= from && x.CreatedAt <= to && x.PayPalRefundId != null))
        {
            records.Add(new ReconciliationRecord("eShop", refund.PayPalRefundId!, refund.OrderId,
                provider.Count == 0 ? "ProviderReportPendingOrEmpty" :
                seenAppIds.Contains(refund.PayPalRefundId!) ? "Matched" : "ApplicationOnly",
                refund.Status, refund.RefundedAmount ?? refund.RequestedAmount, refund.Currency, refund.CreatedAt));
        }
        return new ReconciliationResponse(from, to, provider.Count == 0, records);

        void Add(string? id, int orderId) { if (!string.IsNullOrWhiteSpace(id)) idToOrder[id] = orderId; }
        int? Match(string? id) => id is not null && idToOrder.TryGetValue(id, out var value) ? value : null;
        void AddApp(string? id, Order order, decimal? amount, string? status, DateTimeOffset? at)
        {
            if (id is null) return;
            records.Add(new ReconciliationRecord("eShop", id, order.Id,
                provider.Count == 0 ? "ProviderReportPendingOrEmpty" :
                seenAppIds.Contains(id) ? "Matched" : "ApplicationOnly", status, amount,
                order.Currency, at));
        }
    }

    private async Task RefreshOrRetryRefund(Order order, PaymentRefund refund,
        CancellationToken cancellationToken)
    {
        var wasCompleted = refund.IsCompleted;
        ProviderRefund result;
        if (refund.PayPalRefundId is null)
            result = await _gateway.RefundAsync(refund.CaptureId, refund.RequestedAmount,
                refund.Currency, refund.IdempotencyKey, cancellationToken);
        else if (!refund.IsCompleted)
            result = await _gateway.GetRefundAsync(refund.PayPalRefundId, cancellationToken);
        else return;
        refund.RecordProviderResult(result.RefundId, result.Status, result.Amount);
        if (!wasCompleted && refund.IsCompleted)
            order.RecordCompletedRefund(result.Amount ?? refund.RequestedAmount);
        await _db.SaveChangesAsync(cancellationToken);
    }

    private async Task<Order> OwnedOrder(int id, string buyerId, CancellationToken cancellationToken)
    {
        var order = await _db.Orders.Include(x => x.OrderItems)
            .FirstOrDefaultAsync(x => x.Id == id && x.BuyerId == buyerId, cancellationToken);
        return order ?? throw new PaymentApiException(404, "The order was not found.");
    }

    private async Task<Order> AnyOrder(int id, CancellationToken cancellationToken)
    {
        var order = await _db.Orders.Include(x => x.OrderItems)
            .FirstOrDefaultAsync(x => x.Id == id, cancellationToken);
        return order ?? throw new PaymentApiException(404, "The order was not found.");
    }

    private static void ApplyCapture(Order order, ProviderCapture capture)
    {
        EnsureProviderAmount(order.Total(), capture.Amount, "captured");
        order.RecordCapture(capture.CaptureId, capture.Status, capture.Amount, capture.Fee, capture.Net);
    }

    private static void EnsureProviderAmount(decimal expected, decimal actual, string operation)
    {
        if (expected != actual)
            throw new PaymentApiException(502,
                $"PayPal reported an {operation} amount that does not equal the order total.");
    }

    private static void EnsureCentAmount(decimal amount)
    {
        if (decimal.Round(amount, 2, MidpointRounding.AwayFromZero) != amount)
            throw new PaymentApiException(422, "The amount cannot be represented exactly to the cent.");
    }

    private static void ValidateCard(CardRequest card)
    {
        if (string.IsNullOrWhiteSpace(card.Number) || string.IsNullOrWhiteSpace(card.Expiry) ||
            string.IsNullOrWhiteSpace(card.SecurityCode) || string.IsNullOrWhiteSpace(card.Name) ||
            card.BillingAddress is null || card.BillingAddress.CountryCode.Length != 2)
            throw new PaymentApiException(400, "Complete card and billing-address details are required.");
    }

    private void EnsureConfiguration()
    {
        if (string.IsNullOrWhiteSpace(_options.ClientId) || string.IsNullOrWhiteSpace(_options.ClientSecret) ||
            string.IsNullOrWhiteSpace(_options.Environment) || string.IsNullOrWhiteSpace(_options.Currency))
            throw new PaymentApiException(500, "PayPal configuration is incomplete.");
        if (!string.Equals(_options.Environment, "Sandbox", StringComparison.OrdinalIgnoreCase))
            throw new PaymentApiException(500, "This build is configured to use the PayPal sandbox environment only.");
        _ = Currency();
    }

    private string Currency()
    {
        var currency = _options.Currency.Trim().ToUpperInvariant();
        if (currency.Length != 3) throw new PaymentApiException(500, "PayPal currency must be a three-letter code.");
        return currency;
    }

    private static OrderPaymentDto Dto(Order order) => new(order.Id, order.OrderDate,
        order.PaymentState.ToString(), order.Currency, order.Total(), order.PayPalOrderId,
        order.PayPalAuthorizationId, order.PayPalAuthorizationStatus, order.AuthorizedAmount,
        order.AuthorizationExpiration, order.PayPalCaptureId, order.PayPalCaptureStatus,
        order.CapturedAmount, order.PayPalFee, order.NetProceeds, order.RefundedAmount,
        order.FulfilledAt, order.OrderItems.Select(x => new OrderItemDto(x.ItemOrdered.CatalogItemId,
            x.ItemOrdered.ProductName, x.UnitPrice, x.Units)).ToList());

    private static RefundDto RefundDto(PaymentRefund refund) => new(
        refund.PayPalRefundId ?? refund.Id.ToString(CultureInfo.InvariantCulture), refund.OrderId,
        refund.Status, refund.RequestedAmount, refund.RefundedAmount, refund.Currency);

    private static PaymentMethodDto MethodDto(PaymentMethod method) => new(method.Id, method.Brand,
        method.LastDigits, method.Expiry, method.CardholderName);
}
