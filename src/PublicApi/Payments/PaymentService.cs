using System;
using System.Collections.Generic;
using System.Collections.Concurrent;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentMethodAggregate;
using Microsoft.eShopWeb.Infrastructure.Data;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.PublicApi.Payments;

public sealed class PaymentService
{
    private static readonly ConcurrentDictionary<int, SemaphoreSlim> OrderLocks = new();
    private static readonly Regex CardNumber = new("^[0-9]{13,19}$", RegexOptions.Compiled);
    private static readonly Regex Expiry = new("^[0-9]{4}-(0[1-9]|1[0-2])$", RegexOptions.Compiled);
    private static readonly Regex SecurityCode = new("^[0-9]{3,4}$", RegexOptions.Compiled);
    private static readonly Regex CountryCode = new("^[A-Z]{2}$", RegexOptions.Compiled);

    private readonly CatalogContext _db;
    private readonly IPayPalGateway _paypal;
    private readonly PayPalOptions _options;

    public PaymentService(CatalogContext db, IPayPalGateway paypal, IOptions<PayPalOptions> options)
    {
        _db = db;
        _paypal = paypal;
        _options = options.Value;
    }

    public async Task<PlaceOrderResponse> PlaceOrderAsync(string owner, PlaceOrderRequest request,
        CancellationToken cancellationToken)
    {
        if (request.Items is null || request.Items.Count == 0)
        {
            throw BadRequest("At least one catalog item is required.");
        }
        if (request.Items.Any(x => x.CatalogItemId <= 0 || x.Quantity <= 0))
        {
            throw BadRequest("Catalog item ids and quantities must be positive.");
        }

        var requested = request.Items.GroupBy(x => x.CatalogItemId)
            .ToDictionary(x => x.Key, x => x.Sum(i => i.Quantity));
        var catalog = await _db.CatalogItems.Where(x => requested.Keys.Contains(x.Id))
            .ToListAsync(cancellationToken);
        if (catalog.Count != requested.Count)
        {
            var missing = requested.Keys.Except(catalog.Select(x => x.Id));
            throw new PaymentOperationException(404, $"Catalog item(s) not found: {string.Join(", ", missing)}.");
        }

        var items = catalog.Select(x => new OrderItem(
            new CatalogItemOrdered(x.Id, x.Name, x.PictureUri), x.Price, requested[x.Id])).ToList();
        var supplied = request.ShippingAddress;
        var address = supplied is null
            ? new Address("Not provided", "Not provided", string.Empty, "Not provided", "Not provided")
            : new Address(supplied.Street, supplied.City, supplied.State, supplied.Country, supplied.ZipCode);
        var order = new Order(owner, address, items);
        _db.Orders.Add(order);
        await _db.SaveChangesAsync(cancellationToken);
        return new PlaceOrderResponse(order.Id, order.PaymentStatus.ToString(), order.Total(), _options.Currency);
    }

    public Task<PaymentResponse> PayAsync(string owner, int orderId, PayOrderRequest request,
        CancellationToken cancellationToken) => Locked(orderId, async () =>
    {
        var order = await OwnedOrder(owner, orderId, cancellationToken);
        if (order.PaymentStatus != OrderPaymentStatus.AwaitingPayment)
        {
            if (order.AuthorizationId is not null)
            {
                return Payment(order);
            }
            throw Conflict($"Order {orderId} cannot be paid from state {order.PaymentStatus}.");
        }
        if ((request.Card is null) == (request.PaymentMethodId is null))
        {
            throw BadRequest("Provide either card details or paymentMethodId, but not both.");
        }

        string? vaultId = null;
        CardInput? card = null;
        if (request.PaymentMethodId.HasValue)
        {
            var method = await _db.SavedPaymentMethods.SingleOrDefaultAsync(x =>
                x.Id == request.PaymentMethodId.Value && x.OwnerId == owner && !x.IsDeleted, cancellationToken);
            if (method?.PayPalTokenId is null)
            {
                throw new PaymentOperationException(404, "Saved payment method was not found.");
            }
            vaultId = method.PayPalTokenId;
        }
        else
        {
            card = ValidateCard(request.Card!);
        }

        try
        {
            var result = await _paypal.AuthorizeAsync(order.Id, order.PaymentOperationId, order.Total(),
                _options.Currency, card, vaultId, cancellationToken);
            EnsureAmount(result.Amount, result.Currency, order.Total());
            order.RecordPayPalOrder(result.PayPalOrderId, result.PayPalOrderStatus, _options.Currency);
            order.RecordAuthorization(result.AuthorizationId, result.AuthorizationStatus, result.Amount,
                result.CreatedAt, result.ExpiresAt);
            await _db.SaveChangesAsync(cancellationToken);
            return Payment(order);
        }
        catch (PayPalProviderException ex)
        {
            throw Provider(ex);
        }
    });

    public Task<FulfilResponse> FulfilAsync(int orderId, CancellationToken cancellationToken) => Locked(orderId, async () =>
    {
        var order = await Order(orderId, cancellationToken);
        if (order.CaptureId is not null)
        {
            try
            {
                var known = await _paypal.GetCaptureAsync(order.CaptureId, cancellationToken);
                EnsureAmount(known.Amount, known.Currency, order.Total());
                order.RecordCapture(known.Id, known.Status, known.Amount, known.Fee, known.Net);
                await _db.SaveChangesAsync(cancellationToken);
                return Fulfil(order);
            }
            catch (PayPalProviderException ex) { throw Provider(ex); }
        }
        if (order.PaymentStatus != OrderPaymentStatus.Authorized || order.AuthorizationId is null)
        {
            throw Conflict($"Order {orderId} must have an authorization before fulfilment.");
        }

        var original = order.OriginalAuthorizationCreatedAt ?? order.AuthorizationCreatedAt
            ?? throw Conflict("The authorization date is unavailable; the shopper must authorize again.");
        if (DateTimeOffset.UtcNow >= original.AddDays(30))
        {
            throw Conflict("The PayPal authorization is 30 days old and can no longer be renewed. Ask the shopper to authorize the order again.");
        }

        try
        {
            var current = await _paypal.GetAuthorizationAsync(order.AuthorizationId, order.PayPalOrderId!, cancellationToken);
            if (DateTimeOffset.UtcNow >= original.AddDays(3))
            {
                current = await _paypal.ReauthorizeAsync(order.AuthorizationId, order.PayPalOrderId!,
                    order.PaymentOperationId + "-reauthorize", order.Total(), _options.Currency, cancellationToken);
                EnsureAmount(current.Amount, current.Currency, order.Total());
                order.RecordAuthorization(current.AuthorizationId, current.AuthorizationStatus, current.Amount,
                    current.CreatedAt, current.ExpiresAt);
                await _db.SaveChangesAsync(cancellationToken);
            }

            var capture = await _paypal.CaptureAsync(current.AuthorizationId,
                order.PaymentOperationId + "-capture", order.Id, order.Total(), _options.Currency, cancellationToken);
            EnsureAmount(capture.Amount, capture.Currency, order.Total());
            order.RecordCapture(capture.Id, capture.Status, capture.Amount, capture.Fee, capture.Net);
            await _db.SaveChangesAsync(cancellationToken);
            return Fulfil(order);
        }
        catch (PayPalProviderException ex) { throw Provider(ex); }
    });

    public Task<CancelResponse> CancelAsync(int orderId, CancellationToken cancellationToken) => Locked(orderId, async () =>
    {
        var order = await Order(orderId, cancellationToken);
        if (order.PaymentStatus == OrderPaymentStatus.Cancelled)
        {
            return new CancelResponse(order.Id, order.PaymentStatus.ToString(), order.AuthorizationStatus);
        }
        if (order.CaptureId is not null || order.PaymentStatus is OrderPaymentStatus.Captured
            or OrderPaymentStatus.PartiallyRefunded or OrderPaymentStatus.Refunded)
        {
            throw Conflict("A captured order cannot be cancelled; refund it instead.");
        }
        try
        {
            var status = order.AuthorizationId is null ? "NOT_AUTHORIZED" :
                await _paypal.VoidAsync(order.AuthorizationId, order.PaymentOperationId + "-void", cancellationToken);
            order.Cancel(status);
            await _db.SaveChangesAsync(cancellationToken);
            return new CancelResponse(order.Id, order.PaymentStatus.ToString(), order.AuthorizationStatus);
        }
        catch (PayPalProviderException ex) { throw Provider(ex); }
    });

    public Task<RefundResponse> RefundAsync(string owner, int orderId, RefundOrderRequest request,
        CancellationToken cancellationToken) => Locked(orderId, async () =>
    {
        if (string.IsNullOrWhiteSpace(request.IdempotencyKey) || request.IdempotencyKey.Length > 108)
        {
            throw BadRequest("A refund idempotencyKey of at most 108 characters is required.");
        }
        var order = await OwnedOrder(owner, orderId, cancellationToken);
        if (order.CaptureId is null || !order.CapturedAmount.HasValue)
        {
            throw Conflict("Only a captured order can be refunded.");
        }
        var existing = order.Refunds.SingleOrDefault(x => x.IdempotencyKey == request.IdempotencyKey);
        if (existing?.PayPalRefundId is not null)
        {
            return Refund(order, existing);
        }
        var remaining = order.CapturedAmount.Value - order.RefundedAmount();
        var amount = request.Amount ?? remaining;
        if (amount <= 0 || amount > remaining)
        {
            throw BadRequest($"Refund amount must be positive and no greater than {remaining:0.00} {_options.Currency}.");
        }
        var refund = existing ?? order.AddRefund(request.IdempotencyKey, amount);
        await _db.SaveChangesAsync(cancellationToken);
        try
        {
            var provider = await _paypal.RefundAsync(order.CaptureId, request.IdempotencyKey,
                amount == remaining ? null : amount, _options.Currency, cancellationToken);
            EnsureAmount(provider.Amount, provider.Currency, amount);
            refund.RecordProviderResult(provider.Id, provider.Status, provider.Amount, provider.UpdatedAt);
            order.RefreshRefundState();
            await _db.SaveChangesAsync(cancellationToken);
            return Refund(order, refund);
        }
        catch (PayPalProviderException ex) { throw Provider(ex); }
    });

    public async Task<IReadOnlyList<OrderSummaryResponse>> MyOrdersAsync(string owner,
        CancellationToken cancellationToken)
    {
        var orders = await _db.Orders.AsNoTracking().Where(x => x.BuyerId == owner)
            .Include(x => x.OrderItems).Include(x => x.Refunds)
            .OrderByDescending(x => x.OrderDate).ToListAsync(cancellationToken);
        return orders.Select(Summary).ToList();
    }

    public async Task<PaymentMethodResponse> SaveMethodAsync(string owner, SavePaymentMethodRequest request,
        CancellationToken cancellationToken)
    {
        var card = ValidateCard(request.Card);
        var customerId = await _db.SavedPaymentMethods.Where(x => x.OwnerId == owner && x.PayPalCustomerId != null)
            .Select(x => x.PayPalCustomerId).FirstOrDefaultAsync(cancellationToken);
        var method = new SavedPaymentMethod(owner, Guid.NewGuid().ToString("N"));
        _db.SavedPaymentMethods.Add(method);
        await _db.SaveChangesAsync(cancellationToken);
        try
        {
            var saved = await _paypal.SaveCardAsync(owner, method.RequestId, card, customerId, cancellationToken);
            method.Activate(saved.TokenId, saved.CustomerId, saved.Brand, saved.LastDigits, saved.Expiry, saved.CardType);
            await _db.SaveChangesAsync(cancellationToken);
            return Method(method);
        }
        catch (PayPalProviderException ex) { throw Provider(ex); }
    }

    public async Task<IReadOnlyList<PaymentMethodResponse>> MethodsAsync(string owner,
        CancellationToken cancellationToken)
    {
        var local = await _db.SavedPaymentMethods.AsNoTracking()
            .Where(x => x.OwnerId == owner && !x.IsDeleted && x.PayPalTokenId != null)
            .ToListAsync(cancellationToken);
        if (local.Count == 0) return Array.Empty<PaymentMethodResponse>();
        try
        {
            var provider = await _paypal.ListCardsAsync(local[0].PayPalCustomerId!, cancellationToken);
            var available = provider.Select(x => x.TokenId).ToHashSet(StringComparer.Ordinal);
            return local.Where(x => available.Contains(x.PayPalTokenId!)).Select(Method).ToList();
        }
        catch (PayPalProviderException ex) { throw Provider(ex); }
    }

    public async Task DeleteMethodAsync(string owner, int methodId, CancellationToken cancellationToken)
    {
        var method = await _db.SavedPaymentMethods.SingleOrDefaultAsync(x => x.Id == methodId && x.OwnerId == owner,
            cancellationToken);
        if (method is null || method.IsDeleted || method.PayPalTokenId is null)
            throw new PaymentOperationException(404, "Saved payment method was not found.");
        try
        {
            await _paypal.DeleteCardAsync(method.PayPalTokenId, cancellationToken);
            method.Delete();
            await _db.SaveChangesAsync(cancellationToken);
        }
        catch (PayPalProviderException ex) { throw Provider(ex); }
    }

    public async Task<ReconciliationResponse> ReconcileAsync(DateTimeOffset from, DateTimeOffset to,
        CancellationToken cancellationToken)
    {
        if (from >= to) throw BadRequest("from must be earlier than to.");
        IReadOnlyList<ProviderTransaction> provider;
        try { provider = await _paypal.SearchTransactionsAsync(from, to, cancellationToken); }
        catch (PayPalProviderException ex) { throw Provider(ex); }

        var orders = await _db.Orders.AsNoTracking().Include(x => x.Refunds)
            .Where(x => x.OrderDate <= to && (x.OrderDate >= from || x.FulfilledAt >= from ||
                x.Refunds.Any(r => r.CreatedAt >= from)))
            .ToListAsync(cancellationToken);
        var rows = new List<ReconciliationRow>();
        var matchedOrders = new HashSet<int>();
        foreach (var transaction in provider)
        {
            var order = orders.FirstOrDefault(x => Matches(x, transaction));
            if (order is not null) matchedOrders.Add(order.Id);
            rows.Add(new ReconciliationRow(order is null ? "PAYPAL_ONLY" : "MATCHED", order?.Id,
                transaction.TransactionId, transaction.PayPalReferenceId, transaction.Status,
                transaction.Amount, transaction.Currency, transaction.Fee, transaction.InitiatedAt));
        }
        foreach (var order in orders.Where(x => HasProviderState(x) && !matchedOrders.Contains(x.Id)))
        {
            rows.Add(new ReconciliationRow("ESHOP_ONLY", order.Id, null, order.CaptureId ?? order.AuthorizationId,
                order.CaptureStatus ?? order.AuthorizationStatus, order.CapturedAmount ?? order.AuthorizedAmount,
                order.Currency, order.PayPalFee, order.FulfilledAt ?? order.OrderDate));
        }
        return new ReconciliationResponse(from, to, DateTimeOffset.UtcNow, rows);
    }

    private async Task<Order> OwnedOrder(string owner, int id, CancellationToken ct)
    {
        var order = await _db.Orders.Include(x => x.OrderItems).Include(x => x.Refunds)
            .SingleOrDefaultAsync(x => x.Id == id && x.BuyerId == owner, ct);
        return order ?? throw new PaymentOperationException(404, "Order was not found.");
    }

    private async Task<Order> Order(int id, CancellationToken ct)
    {
        var order = await _db.Orders.Include(x => x.OrderItems).Include(x => x.Refunds)
            .SingleOrDefaultAsync(x => x.Id == id, ct);
        return order ?? throw new PaymentOperationException(404, "Order was not found.");
    }

    private static async Task<T> Locked<T>(int id, Func<Task<T>> action)
    {
        var gate = OrderLocks.GetOrAdd(id, _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync();
        try { return await action(); }
        finally { gate.Release(); }
    }

    private void EnsureAmount(decimal amount, string currency, decimal expected)
    {
        if (amount != expected || !string.Equals(currency, _options.Currency, StringComparison.OrdinalIgnoreCase))
            throw new PaymentOperationException(502, "PayPal returned an amount or currency that does not match the order.");
    }

    private static CardInput ValidateCard(CardDetailsRequest card)
    {
        if (card is null || string.IsNullOrWhiteSpace(card.Name) || !CardNumber.IsMatch(card.Number ?? "") ||
            !Expiry.IsMatch(card.Expiry ?? "") || !SecurityCode.IsMatch(card.SecurityCode ?? "") ||
            card.BillingAddress is null || !CountryCode.IsMatch(card.BillingAddress.CountryCode ?? ""))
        {
            throw BadRequest("Card requires a name, 13-19 digit number, YYYY-MM expiry, 3-4 digit security code, and two-letter uppercase country code.");
        }
        return new CardInput(card.Name, card.Number, card.Expiry, card.SecurityCode,
            new CardBillingAddress(card.BillingAddress.CountryCode, card.BillingAddress.AddressLine1,
                card.BillingAddress.AddressLine2, card.BillingAddress.AdminArea1,
                card.BillingAddress.AdminArea2, card.BillingAddress.PostalCode));
    }

    private static PaymentResponse Payment(Order x) => new(x.Id, x.PaymentStatus.ToString(), x.PayPalOrderId,
        x.AuthorizationId, x.AuthorizationStatus, x.AuthorizedAmount, x.Currency ?? string.Empty);
    private static FulfilResponse Fulfil(Order x) => new(x.Id, x.PaymentStatus.ToString(), x.CaptureId!,
        x.CaptureStatus!, x.CapturedAmount!.Value, x.PayPalFee, x.NetProceeds, x.Currency!);
    private static RefundResponse Refund(Order x, PaymentRefund r) => new(r.Id, x.Id, r.PayPalRefundId!,
        r.Status, r.Amount, x.Currency!);
    private static PaymentMethodResponse Method(SavedPaymentMethod x) => new(x.Id, x.Brand, x.LastDigits, x.Expiry, x.CardType);
    private static OrderSummaryResponse Summary(Order x) => new(x.Id, x.OrderDate, x.PaymentStatus.ToString(),
        x.Total(), x.Currency, x.PayPalOrderId, x.AuthorizationId, x.AuthorizationStatus, x.CaptureId,
        x.CaptureStatus, x.CapturedAmount, x.PayPalFee, x.NetProceeds, x.RefundedAmount(),
        x.Refunds.Select(r => new RefundSummaryResponse(r.Id, r.PayPalRefundId, r.Status, r.Amount)).ToList());
    private static bool HasProviderState(Order x) => x.PayPalOrderId is not null || x.AuthorizationId is not null || x.CaptureId is not null;
    private static bool Matches(Order x, ProviderTransaction t) =>
        new[] { x.PayPalOrderId, x.AuthorizationId, x.CaptureId }
            .Concat(x.Refunds.Select(r => r.PayPalRefundId))
            .Where(v => v is not null)
            .Any(v => v == t.TransactionId || v == t.PayPalReferenceId) ||
        t.InvoiceId == $"eshop-order-{x.Id}" || t.CustomField == x.Id.ToString(CultureInfo.InvariantCulture);
    private static PaymentOperationException BadRequest(string message) => new(400, message);
    private static PaymentOperationException Conflict(string message) => new(409, message);
    private static PaymentOperationException Provider(PayPalProviderException ex) => new(
        ex.StatusCode is >= 400 and < 500 ? ex.StatusCode.Value : 502,
        ex.OutcomeUnknown ? ex.Message + " The outcome may be unknown; retry the same operation." : ex.Message,
        ex.DebugId);
}
