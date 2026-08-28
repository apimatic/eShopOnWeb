using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.eShopWeb.ApplicationCore.Entities;
using Microsoft.eShopWeb.ApplicationCore.Entities.BuyerAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.Infrastructure.Data;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.PublicApi.Payments;

public sealed class PaymentApplicationService
{
    private readonly CatalogContext _db;
    private readonly IPayPalGateway _payPal;
    private readonly OperationLock _operationLock;
    private readonly PayPalOptions _options;

    public PaymentApplicationService(CatalogContext db, IPayPalGateway payPal,
        OperationLock operationLock, IOptions<PayPalOptions> options)
    {
        _db = db;
        _payPal = payPal;
        _operationLock = operationLock;
        _options = options.Value;
    }

    public async Task<OrderCreatedResponse> PlaceOrderAsync(string buyerId, PlaceOrderRequest request,
        CancellationToken cancellationToken)
    {
        RequireBuyer(buyerId);
        if (request.Items == null || request.Items.Count == 0 || request.ShippingAddress == null)
            throw BadRequest("invalid_order", "At least one catalog item and a shipping address are required.");
        if (request.Items.Any(x => x.CatalogItemId <= 0 || x.Quantity <= 0))
            throw BadRequest("invalid_quantity", "Catalog item ids and quantities must be positive.");

        var quantities = request.Items
            .GroupBy(x => x.CatalogItemId)
            .ToDictionary(x => x.Key, x => x.Sum(y => y.Quantity));
        var ids = quantities.Keys.ToArray();
        var catalogItems = await _db.CatalogItems.Where(x => ids.Contains(x.Id)).ToListAsync(cancellationToken);
        if (catalogItems.Count != ids.Length)
            throw BadRequest("catalog_item_not_found", "One or more catalog items do not exist.");

        var orderItems = catalogItems.Select(item => new OrderItem(
            new CatalogItemOrdered(item.Id, item.Name, item.PictureUri), item.Price, quantities[item.Id])).ToList();
        var address = request.ShippingAddress;
        var order = new Order(buyerId,
            new Address(address.Street, address.City, address.State ?? string.Empty,
                address.Country, address.ZipCode),
            orderItems, _options.Currency, Guid.NewGuid());

        _db.Orders.Add(order);
        await _db.SaveChangesAsync(cancellationToken);
        return new OrderCreatedResponse(order.Id, order.OrderTotal, order.Currency!, order.PaymentStatus.ToString());
    }

    public async Task<PaymentResponse> PayAsync(string buyerId, int orderId, PayOrderRequest request,
        CancellationToken cancellationToken)
    {
        RequireBuyer(buyerId);
        await using var held = await _operationLock.AcquireAsync($"order:{orderId}", cancellationToken);
        var order = await OwnedOrder(orderId, buyerId, cancellationToken);
        if (order.PaymentStatus == OrderPaymentStatus.Authorized)
            return Payment(order);
        if (order.PaymentStatus != OrderPaymentStatus.AwaitingPayment)
            throw Conflict("order_not_payable", $"An order in state {order.PaymentStatus} cannot be authorized.");
        if ((request.Card == null) == (request.PaymentMethodId == null))
            throw BadRequest("payment_source_required", "Supply either card details or one saved paymentMethodId.");

        PaymentMethod? method = null;
        if (request.PaymentMethodId.HasValue)
        {
            method = await _db.PaymentMethods.SingleOrDefaultAsync(x =>
                x.Id == request.PaymentMethodId.Value && x.BuyerId == buyerId && x.IsActive, cancellationToken);
            if (method == null) throw NotFound("payment_method_not_found", "The saved card was not found.");
        }

        var correlation = order.PaymentCorrelationId?.ToString("N")
            ?? throw Conflict("payment_not_enabled", "This order was not created through the payment API.");
        var invoiceId = $"eshop-{correlation}";
        var amount = Money(order.OrderTotal);
        if (order.PayPalOrderId == null)
        {
            var providerOrder = await _payPal.CreateOrderAsync(amount, order.Currency!, invoiceId, invoiceId,
                RequestId("order", correlation), cancellationToken);
            order.RecordPayPalOrder(providerOrder.Id, providerOrder.Status);
            await _db.SaveChangesAsync(cancellationToken);
        }

        var payPalOrderId = order.PayPalOrderId
            ?? throw Conflict("paypal_order_missing", "The PayPal order could not be established.");
        var authorization = await _payPal.AuthorizeAsync(payPalOrderId,
            request.Card == null ? null : Card(request.Card), method?.ProviderTokenId,
            RequestId("auth", correlation), cancellationToken);
        order.RecordPayPalOrder(payPalOrderId, authorization.OrderStatus);
        if (string.Equals(authorization.OrderStatus, "PAYER_ACTION_REQUIRED", StringComparison.Ordinal))
        {
            await _db.SaveChangesAsync(cancellationToken);
            throw Conflict("payer_action_required",
                "PayPal requires a browser challenge. Stop this headless flow; no authorization was accepted.");
        }
        EnsureMoney(order, authorization.Amount, authorization.Currency, "authorized");
        if (authorization.Status is not "CREATED" and not "PENDING")
            throw Conflict("authorization_rejected", $"PayPal returned authorization status {authorization.Status}.");

        order.RecordAuthorization(authorization.Id, authorization.Status, authorization.Amount,
            authorization.CreateTime, authorization.UpdateTime, authorization.ExpirationTime, method?.Id);
        await _db.SaveChangesAsync(cancellationToken);
        return Payment(order);
    }

    public async Task<FulfilmentResponse> FulfilAsync(int orderId, CancellationToken cancellationToken)
    {
        await using var held = await _operationLock.AcquireAsync($"order:{orderId}", cancellationToken);
        var order = await AnyOrder(orderId, cancellationToken);
        if (order.CaptureId != null)
        {
            if (order.PaymentStatus is OrderPaymentStatus.Fulfilled
                or OrderPaymentStatus.PartiallyRefunded or OrderPaymentStatus.Refunded)
                return Fulfilment(order);
            var currentCapture = await _payPal.GetCaptureAsync(order.CaptureId, cancellationToken);
            EnsureMoney(order, currentCapture.Amount, currentCapture.Currency, "captured");
            order.RecordCapture(currentCapture.Id, currentCapture.Status, currentCapture.Amount,
                currentCapture.GrossAmount, currentCapture.Fee, currentCapture.NetAmount,
                currentCapture.CreateTime, currentCapture.UpdateTime);
            await _db.SaveChangesAsync(cancellationToken);
            return Fulfilment(order);
        }
        if (order.PaymentStatus != OrderPaymentStatus.Authorized || order.AuthorizationId == null)
            throw Conflict("order_not_fulfillable", "The order must have an active authorization before fulfilment.");

        var authorization = await _payPal.GetAuthorizationAsync(order.AuthorizationId, cancellationToken);
        EnsureMoney(order, authorization.Amount, authorization.Currency, "authorized");
        order.RefreshAuthorization(authorization.Id, authorization.Status, authorization.Amount,
            authorization.CreateTime, authorization.UpdateTime, authorization.ExpirationTime);

        var created = ParseProviderTime(authorization.CreateTime) ?? order.OrderDate;
        if (DateTimeOffset.UtcNow >= created.AddDays(29))
            throw Conflict("authorization_renewal_required",
                "The PayPal authorization can no longer be renewed. Ask the shopper to authorize the order again.");
        var expires = ParseProviderTime(authorization.ExpirationTime);
        if (DateTimeOffset.UtcNow >= created.AddDays(3) || (expires.HasValue && expires <= DateTimeOffset.UtcNow))
        {
            authorization = await _payPal.ReauthorizeAsync(authorization.Id, Money(order.OrderTotal),
                order.Currency!, RequestId("reauth", order.PaymentCorrelationId!.Value.ToString("N")), cancellationToken);
            EnsureMoney(order, authorization.Amount, authorization.Currency, "reauthorized");
            order.RefreshAuthorization(authorization.Id, authorization.Status, authorization.Amount,
                authorization.CreateTime, authorization.UpdateTime, authorization.ExpirationTime);
            await _db.SaveChangesAsync(cancellationToken);
        }

        if (authorization.Status is not "CREATED" and not "PENDING")
            throw Conflict("authorization_not_capturable",
                $"PayPal authorization {authorization.Id} is {authorization.Status}; obtain a new shopper authorization.");
        var capture = await _payPal.CaptureAsync(authorization.Id, Money(order.OrderTotal), order.Currency!,
            RequestId("capture", order.PaymentCorrelationId!.Value.ToString("N")), cancellationToken);
        EnsureMoney(order, capture.Amount, capture.Currency, "captured");
        if (capture.Status is not "COMPLETED" and not "PENDING")
            throw Conflict("capture_not_completed", $"PayPal returned capture status {capture.Status}.");
        order.RecordCapture(capture.Id, capture.Status, capture.Amount, capture.GrossAmount,
            capture.Fee, capture.NetAmount, capture.CreateTime, capture.UpdateTime);
        await _db.SaveChangesAsync(cancellationToken);
        return Fulfilment(order);
    }

    public async Task<CancelResponse> CancelAsync(int orderId, CancellationToken cancellationToken)
    {
        await using var held = await _operationLock.AcquireAsync($"order:{orderId}", cancellationToken);
        var order = await AnyOrder(orderId, cancellationToken);
        if (order.PaymentStatus == OrderPaymentStatus.Cancelled)
            return new CancelResponse(order.Id, order.PaymentStatus.ToString(), order.AuthorizationStatus ?? "VOIDED");
        if (order.PaymentStatus == OrderPaymentStatus.AwaitingPayment)
        {
            order.MarkCancelled("NOT_AUTHORIZED");
            await _db.SaveChangesAsync(cancellationToken);
            return new CancelResponse(order.Id, order.PaymentStatus.ToString(), order.AuthorizationStatus!);
        }
        if (order.CaptureId != null || order.PaymentStatus != OrderPaymentStatus.Authorized || order.AuthorizationId == null)
            throw Conflict("order_not_cancellable", "Only an authorized, unfulfilled order can be cancelled.");

        var result = await _payPal.VoidAsync(order.AuthorizationId,
            RequestId("void", order.PaymentCorrelationId!.Value.ToString("N")), cancellationToken);
        if (result.Status != "VOIDED")
            throw Conflict("authorization_not_voided", $"PayPal returned authorization status {result.Status}.");
        order.MarkCancelled(result.Status);
        await _db.SaveChangesAsync(cancellationToken);
        return new CancelResponse(order.Id, order.PaymentStatus.ToString(), result.Status);
    }

    public async Task<RefundCreatedResponse> RefundAsync(string buyerId, int orderId,
        RefundOrderRequest request, CancellationToken cancellationToken)
    {
        RequireBuyer(buyerId);
        if (string.IsNullOrWhiteSpace(request.IdempotencyKey) || request.IdempotencyKey.Length > 128)
            throw BadRequest("invalid_idempotency_key", "An idempotency key of 1 to 128 characters is required.");
        await using var held = await _operationLock.AcquireAsync($"order:{orderId}", cancellationToken);
        var order = await OwnedOrder(orderId, buyerId, cancellationToken);
        if (order.CaptureId == null || order.CapturedAmount == null)
            throw Conflict("order_not_refundable", "The order has no captured payment to refund.");

        var existing = order.Refunds.SingleOrDefault(x => x.IdempotencyKey == request.IdempotencyKey);
        if (existing?.ProviderRefundId != null)
            return RefundResponse(order, existing);
        if (order.PaymentStatus is not OrderPaymentStatus.Fulfilled
            and not OrderPaymentStatus.PartiallyRefunded and not OrderPaymentStatus.Refunded)
            throw Conflict("order_not_refundable", "The captured payment is not complete and cannot be refunded.");
        var remaining = order.RefundableAmount + (existing?.Amount ?? 0m);
        var amount = request.Amount ?? remaining;
        if (existing != null && amount != existing.Amount)
            throw Conflict("idempotency_key_reused",
                "That idempotency key is already reserved for a different refund amount.");
        if (amount <= 0m || decimal.Round(amount, 2) != amount || amount > remaining)
            throw BadRequest("invalid_refund_amount", "The amount must be positive and no greater than the remaining captured amount.");

        var fullProviderRefund = request.Amount == null
            && order.ReservedOrRefundedAmount - (existing?.Amount ?? 0m) == 0m
            && amount == order.CapturedAmount.Value;
        var refund = existing ?? order.ReserveRefund(request.IdempotencyKey, amount);
        try
        {
            await _db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            throw Conflict("refund_concurrency_conflict",
                "Another refund changed this order. Retry after refreshing its payment state.");
        }

        try
        {
            var provider = await _payPal.RefundAsync(order.CaptureId,
                fullProviderRefund ? null : Money(amount), order.Currency!, request.IdempotencyKey, cancellationToken);
            if (provider.Amount != amount || !SameCurrency(provider.Currency, order.Currency!))
                throw Conflict("refund_amount_mismatch", "PayPal returned a refund amount that did not match the request.");
            refund.RecordProviderResult(provider.Id, provider.Status, provider.Amount, provider.Currency,
                provider.CreateTime, provider.UpdateTime);
            order.RecalculateRefundStatus();
            await _db.SaveChangesAsync(cancellationToken);
            return RefundResponse(order, refund);
        }
        catch (Exception ex) when (ex is not HttpRequestException and not TaskCanceledException and not JsonException)
        {
            refund.MarkFailed();
            await _db.SaveChangesAsync(CancellationToken.None);
            throw;
        }
    }

    public async Task<PaymentMethodResponse> SavePaymentMethodAsync(string buyerId,
        SavePaymentMethodRequest request, CancellationToken cancellationToken)
    {
        RequireBuyer(buyerId);
        if (request.Card == null) throw BadRequest("card_required", "Card details are required.");
        await using var held = await _operationLock.AcquireAsync($"customer:{buyerId}", cancellationToken);
        var known = await _db.PaymentMethods.Where(x => x.BuyerId == buyerId)
            .OrderByDescending(x => x.Id).FirstOrDefaultAsync(cancellationToken);
        var provider = await _payPal.SaveCardAsync(CustomerReference(buyerId), known?.ProviderCustomerId,
            Card(request.Card), RequestId("vault", Guid.NewGuid().ToString("N")), cancellationToken);
        var method = new PaymentMethod(buyerId, provider.Id, provider.CustomerId,
            provider.Brand, provider.LastDigits, provider.Expiry, provider.CardType);
        _db.PaymentMethods.Add(method);
        await _db.SaveChangesAsync(cancellationToken);
        return PaymentMethod(method);
    }

    public async Task<PaymentMethodsResponse> ListPaymentMethodsAsync(string buyerId,
        CancellationToken cancellationToken)
    {
        RequireBuyer(buyerId);
        await using var held = await _operationLock.AcquireAsync($"customer:{buyerId}", cancellationToken);
        var local = await _db.PaymentMethods.Where(x => x.BuyerId == buyerId && x.IsActive)
            .OrderBy(x => x.Id).ToListAsync(cancellationToken);
        if (local.Count == 0) return new PaymentMethodsResponse(Array.Empty<PaymentMethodResponse>());

        var provider = await _payPal.ListCardsAsync(local[0].ProviderCustomerId, cancellationToken);
        var byId = provider.ToDictionary(x => x.Id, StringComparer.Ordinal);
        foreach (var method in local)
        {
            if (byId.TryGetValue(method.ProviderTokenId, out var found))
                method.RefreshSafeDetails(found.Brand, found.LastDigits, found.Expiry, found.CardType);
        }
        await _db.SaveChangesAsync(cancellationToken);
        return new PaymentMethodsResponse(local.Where(x => x.IsActive).Select(PaymentMethod).ToList());
    }

    public async Task DeletePaymentMethodAsync(string buyerId, int paymentMethodId,
        CancellationToken cancellationToken)
    {
        RequireBuyer(buyerId);
        await using var held = await _operationLock.AcquireAsync($"customer:{buyerId}", cancellationToken);
        var method = await _db.PaymentMethods.SingleOrDefaultAsync(x =>
            x.Id == paymentMethodId && x.BuyerId == buyerId && x.IsActive, cancellationToken);
        if (method == null) throw NotFound("payment_method_not_found", "The saved card was not found.");
        await _payPal.DeleteCardAsync(method.ProviderTokenId, cancellationToken);
        method.Deactivate();
        await _db.SaveChangesAsync(cancellationToken);
    }

    public async Task<MyOrdersResponse> MyOrdersAsync(string buyerId, CancellationToken cancellationToken)
    {
        RequireBuyer(buyerId);
        var orders = await _db.Orders.AsNoTracking().Where(x => x.BuyerId == buyerId)
            .Include(x => x.Refunds).OrderByDescending(x => x.OrderDate).ToListAsync(cancellationToken);
        return new MyOrdersResponse(orders.Select(OrderResponse).ToList());
    }

    public async Task<ReconciliationResponse> ReconcileAsync(DateTimeOffset from, DateTimeOffset to,
        CancellationToken cancellationToken)
    {
        if (from > to) throw BadRequest("invalid_range", "from must be before or equal to to.");
        var now = DateTimeOffset.UtcNow;
        if (to > now) throw BadRequest("future_range", "to cannot be in the future.");
        if (from < now.AddYears(-3))
            throw BadRequest("range_not_available", "PayPal transaction history is available for the previous three years.");
        var provider = await _payPal.SearchTransactionsAsync(from, to, cancellationToken);
        var orders = await _db.Orders.AsNoTracking().Include(x => x.Refunds).ToListAsync(cancellationToken);
        var rows = new List<ReconciliationRow>();
        var matchedProviderIds = new HashSet<string>(StringComparer.Ordinal);

        foreach (var transaction in provider)
        {
            var order = orders.FirstOrDefault(x => ProviderIds(x).Contains(transaction.TransactionId)
                || ProviderIds(x).Contains(transaction.ReferenceId)
                || x.PaymentCorrelationId.HasValue &&
                   (transaction.InvoiceId == $"eshop-{x.PaymentCorrelationId.Value:N}"
                    || transaction.CustomField == $"eshop-{x.PaymentCorrelationId.Value:N}"));
            if (order != null)
            {
                foreach (var id in ProviderIds(order).Where(id => id != null
                             && (id == transaction.TransactionId || id == transaction.ReferenceId)))
                    matchedProviderIds.Add(id!);
            }
            rows.Add(new ReconciliationRow("PayPal", order == null ? "PayPalOnly" : "Matched",
                order?.Id, transaction.TransactionId, transaction.ReferenceId, transaction.EventCode,
                transaction.Status, transaction.Amount, transaction.Currency, transaction.Fee,
                transaction.InvoiceId, transaction.InitiatedAt));
        }

        foreach (var order in orders)
        {
            if (order.CaptureId != null && !matchedProviderIds.Contains(order.CaptureId)
                && IsInRange(ParseProviderTime(order.CaptureCreateTime) ?? order.OrderDate, from, to))
            {
                rows.Add(new ReconciliationRow("eShop", "EShopOnly", order.Id, order.CaptureId,
                    order.AuthorizationId, "CAPTURE", order.CaptureStatus, order.CapturedAmount,
                    order.Currency, order.PayPalFee, Invoice(order), order.CaptureCreateTime));
            }
            foreach (var refund in order.Refunds.Where(x => x.ProviderRefundId != null
                         && !matchedProviderIds.Contains(x.ProviderRefundId)
                         && IsInRange(ParseProviderTime(x.ProviderCreateTime) ?? x.CreatedAt, from, to)))
            {
                rows.Add(new ReconciliationRow("eShop", "EShopOnly", order.Id, refund.ProviderRefundId,
                    order.CaptureId, "REFUND", refund.ProviderStatus, -refund.Amount,
                    refund.Currency, null, Invoice(order), refund.ProviderCreateTime));
            }
        }
        return new ReconciliationResponse(from, to, rows);
    }

    private async Task<Order> OwnedOrder(int orderId, string buyerId, CancellationToken cancellationToken) =>
        await _db.Orders.Include(x => x.Refunds).Include(x => x.OrderItems)
            .SingleOrDefaultAsync(x => x.Id == orderId && x.BuyerId == buyerId, cancellationToken)
        ?? throw NotFound("order_not_found", "The order was not found.");

    private async Task<Order> AnyOrder(int orderId, CancellationToken cancellationToken) =>
        await _db.Orders.Include(x => x.Refunds).Include(x => x.OrderItems)
            .SingleOrDefaultAsync(x => x.Id == orderId, cancellationToken)
        ?? throw NotFound("order_not_found", "The order was not found.");

    private static PaymentResponse Payment(Order order) => new(order.Id, order.PaymentStatus.ToString(),
        order.PayPalOrderId, order.AuthorizationId, order.AuthorizedAmount, order.AuthorizationStatus);

    private static FulfilmentResponse Fulfilment(Order order) => new(order.Id, order.PaymentStatus.ToString(),
        order.CaptureId!, order.CaptureStatus!, order.CapturedAmount!.Value,
        order.PayPalGrossAmount!.Value, order.PayPalFee, order.MerchantNetAmount, order.Currency!);

    private static RefundCreatedResponse RefundResponse(Order order, PaymentRefund refund) =>
        new(refund.ProviderRefundId!, order.Id, refund.ProviderStatus, refund.Amount,
            refund.Currency, order.RefundableAmount);

    private static PaymentMethodResponse PaymentMethod(PaymentMethod method) =>
        new(method.Id, method.Brand, method.LastDigits, method.Expiry, method.CardType);

    private static OrderResponse OrderResponse(Order order) => new(order.Id, order.OrderDate,
        order.OrderTotal == 0m ? order.Total() : order.OrderTotal, order.Currency, order.PaymentStatus.ToString(),
        order.AuthorizationId, order.AuthorizationStatus, order.CaptureId, order.CaptureStatus,
        order.CapturedAmount, order.PayPalFee, order.MerchantNetAmount, order.RefundableAmount,
        order.Refunds.Select(x => new RefundResponse(x.ProviderRefundId, x.ProviderStatus, x.Amount, x.Currency)).ToList());

    private static CardInput Card(CardRequestDto card)
    {
        if (string.IsNullOrWhiteSpace(card.Name) || string.IsNullOrWhiteSpace(card.Number)
            || string.IsNullOrWhiteSpace(card.Expiry) || string.IsNullOrWhiteSpace(card.SecurityCode)
            || card.BillingAddress == null || string.IsNullOrWhiteSpace(card.BillingAddress.CountryCode))
            throw BadRequest("invalid_card", "Complete card and billing-address details are required.");
        var a = card.BillingAddress;
        return new CardInput(card.Name, card.Number, card.Expiry, card.SecurityCode,
            new CardAddressInput(a.AddressLine1, a.AddressLine2, a.City, a.State, a.PostalCode,
                a.CountryCode.ToUpperInvariant()));
    }

    private static string Money(decimal amount) => amount.ToString("0.00", CultureInfo.InvariantCulture);
    private static bool SameCurrency(string left, string right) =>
        string.Equals(left, right, StringComparison.OrdinalIgnoreCase);
    private static void EnsureMoney(Order order, decimal amount, string currency, string verb)
    {
        if (amount != order.OrderTotal || !SameCurrency(currency, order.Currency!))
            throw Conflict("provider_amount_mismatch",
                $"The PayPal {verb} amount did not exactly match the order total and currency.");
    }

    private static DateTimeOffset? ParseProviderTime(string? value) =>
        DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var parsed)
            ? parsed : null;

    private static string CustomerReference(string buyerId) => $"eshop-{Hash(buyerId)[..32]}";
    private static string RequestId(string operation, string value) => $"eso-{operation[..Math.Min(4, operation.Length)]}-{Hash(value)[..28]}";
    private static string Hash(string value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();
    private static HashSet<string?> ProviderIds(Order order) => new(new[]
    {
        order.PayPalOrderId, order.AuthorizationId, order.CaptureId
    }.Concat(order.Refunds.Select(x => x.ProviderRefundId)), StringComparer.Ordinal);
    private static bool IsInRange(DateTimeOffset value, DateTimeOffset from, DateTimeOffset to) =>
        value >= from && value <= to;
    private static string? Invoice(Order order) => order.PaymentCorrelationId.HasValue
        ? $"eshop-{order.PaymentCorrelationId.Value:N}" : null;

    private static void RequireBuyer(string buyerId)
    {
        if (string.IsNullOrWhiteSpace(buyerId)) throw new PaymentApiException((int)HttpStatusCode.Unauthorized,
            "authentication_required", "A signed-in shopper is required.");
    }
    private static PaymentApiException BadRequest(string code, string message) =>
        new((int)HttpStatusCode.BadRequest, code, message);
    private static PaymentApiException NotFound(string code, string message) =>
        new((int)HttpStatusCode.NotFound, code, message);
    private static PaymentApiException Conflict(string code, string message) =>
        new((int)HttpStatusCode.Conflict, code, message);
}
