using System;
using System.Collections.Concurrent;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.eShopWeb.ApplicationCore.Entities;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.Infrastructure.Data;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.PublicApi.Payments;

public sealed class PaymentService
{
    private static readonly ConcurrentDictionary<int, SemaphoreSlim> OrderLocks = new();
    private static readonly Regex ExpiryPattern = new("^[0-9]{4}-(0[1-9]|1[0-2])$", RegexOptions.Compiled);
    private readonly CatalogContext _db;
    private readonly IPayPalClient _payPal;
    private readonly PayPalOptions _options;

    public PaymentService(CatalogContext db, IPayPalClient payPal, IOptions<PayPalOptions> options)
    {
        _db = db;
        _payPal = payPal;
        _options = options.Value;
    }

    public async Task<Order> PayAsync(int orderId, string buyerId, CardDetails? card,
        int? paymentMethodId, CancellationToken cancellationToken)
    {
        if ((card is null) == (paymentMethodId is null))
            throw BadRequest("Supply exactly one of card or paymentMethodId.");
        return await WithOrderLock(orderId, async () =>
        {
            var order = await LoadOrderAsync(orderId, cancellationToken);
            EnsureOwner(order, buyerId);
            if (card is not null) ValidateCard(card);
            if (order.FulfillmentStatus == OrderFulfillmentStatus.Cancelled)
                throw Conflict("A cancelled order cannot be paid.");
            if (order.PaymentStatus is OrderPaymentStatus.Authorized or OrderPaymentStatus.AuthorizationPending)
                return order;
            if (order.PaymentStatus is not OrderPaymentStatus.AwaitingPayment)
                throw Conflict($"Order {orderId} cannot be paid while its payment state is {order.PaymentStatus}.");

            string? vaultId = null;
            if (paymentMethodId.HasValue)
            {
                var method = await _db.PaymentMethods.SingleOrDefaultAsync(x =>
                    x.Id == paymentMethodId.Value && x.BuyerId == buyerId, cancellationToken);
                if (method is null || !method.IsActive)
                    throw NotFound("The saved payment method does not exist or has been removed.");
                vaultId = method.PayPalTokenId;
            }

            var total = Money(order.Total());
            if (order.PayPalOrderId is null)
            {
                var created = await _payPal.CreateOrderAsync(order.Id, order.PaymentReference, total,
                    _options.Currency, RequestId("order", order.PaymentReference), cancellationToken);
                order.RecordPayPalOrder(created.Id, created.Status, _options.Currency);
                await _db.SaveChangesAsync(cancellationToken);
            }

            var authorization = await _payPal.AuthorizeOrderAsync(order.PayPalOrderId!, card, vaultId,
                RequestId("authorize", order.PaymentReference), cancellationToken);
            EnsureMoneyMatches(total, authorization.Amount, authorization.Currency, "authorization");
            if (authorization.AuthorizationStatus is not ("CREATED" or "PENDING"))
                throw Conflict($"PayPal did not place a usable hold; authorization status is {authorization.AuthorizationStatus}.");
            order.RecordAuthorization(authorization.AuthorizationId, authorization.AuthorizationStatus,
                authorization.CreatedAt, authorization.ExpiresAt, paymentMethodId, authorization.OrderStatus);
            await _db.SaveChangesAsync(cancellationToken);
            return order;
        }, cancellationToken);
    }

    public async Task<Order> FulfilAsync(int orderId, CancellationToken cancellationToken)
    {
        return await WithOrderLock(orderId, async () =>
        {
            var order = await LoadOrderAsync(orderId, cancellationToken);
            if (order.FulfillmentStatus == OrderFulfillmentStatus.Fulfilled) return order;
            if (order.FulfillmentStatus == OrderFulfillmentStatus.Cancelled)
                throw Conflict("A cancelled order cannot be fulfilled.");
            if (order.PayPalAuthorizationId is null ||
                order.PaymentStatus is not (OrderPaymentStatus.Authorized or
                    OrderPaymentStatus.AuthorizationPending or OrderPaymentStatus.CapturePending))
                throw Conflict("The order must have an authorized payment before it can be fulfilled.");

            PayPalCaptureResult capture;
            if (order.PayPalCaptureId is not null)
            {
                capture = await _payPal.GetCaptureAsync(order.PayPalCaptureId, cancellationToken);
            }
            else
            {
                var authorization = await _payPal.GetAuthorizationAsync(order.PayPalAuthorizationId,
                    cancellationToken);
                order.RecordAuthorizationStatus(authorization.Status, authorization.ExpiresAt);
                if (authorization.Status == "PENDING")
                {
                    await _db.SaveChangesAsync(cancellationToken);
                    throw Conflict("PayPal is still reviewing the authorization. Retry fulfilment after the authorization leaves PENDING status.");
                }
                if (authorization.Status is not ("CREATED" or "PARTIALLY_CAPTURED" or "CAPTURED"))
                {
                    await _db.SaveChangesAsync(cancellationToken);
                    throw Conflict($"The PayPal authorization is {authorization.Status}; ask the shopper to authorize the order again before fulfilment.");
                }

                var now = DateTimeOffset.UtcNow;
                var createdAt = authorization.CreatedAt ?? order.AuthorizationCreatedAt;
                var expiresAt = authorization.ExpiresAt ?? order.AuthorizationExpiresAt;
                if (expiresAt.HasValue && expiresAt.Value <= now)
                    throw Conflict("The PayPal authorization has expired and can no longer be renewed. Ask the shopper to authorize the order again.");

                if (createdAt.HasValue && now >= createdAt.Value.AddDays(3) && !order.AuthorizationWasRenewed)
                {
                    try
                    {
                        var renewed = await _payPal.ReauthorizeAsync(order.PayPalAuthorizationId,
                            Money(order.Total()), _options.Currency, RequestId("reauthorize", order.PaymentReference),
                            cancellationToken);
                        EnsureMoneyMatches(Money(order.Total()), renewed.Amount, renewed.Currency,
                            "reauthorization");
                        order.RecordAuthorization(renewed.Id, renewed.Status, renewed.CreatedAt,
                            renewed.ExpiresAt, order.PaymentMethodId, order.PayPalOrderStatus ?? "COMPLETED", true);
                        await _db.SaveChangesAsync(cancellationToken);
                    }
                    catch (PayPalApiException ex) when (ex.StatusCode is HttpStatusCode.UnprocessableEntity or HttpStatusCode.BadRequest)
                    {
                        throw Conflict($"PayPal could not renew the stale authorization ({ex.Issue ?? ex.ErrorName}). Ask the shopper to authorize the order again.");
                    }
                }

                capture = await _payPal.CaptureAsync(order.PayPalAuthorizationId!, Money(order.Total()),
                    _options.Currency, PayPalClient.InvoiceId(order.PaymentReference),
                    RequestId("capture", order.PaymentReference),
                    cancellationToken);
            }

            EnsureMoneyMatches(Money(order.Total()), capture.Amount, capture.Currency, "capture");
            order.RecordCapture(capture.Id, capture.Status, capture.Amount, capture.PayPalFee,
                capture.NetAmount, capture.CreatedAt);
            await _db.SaveChangesAsync(cancellationToken);
            if (capture.Status != "COMPLETED")
                throw Conflict($"PayPal capture {capture.Id} is {capture.Status}; funds are not yet confirmed. Retry fulfilment later.");
            return order;
        }, cancellationToken);
    }

    public async Task<Order> CancelAsync(int orderId, CancellationToken cancellationToken)
    {
        return await WithOrderLock(orderId, async () =>
        {
            var order = await LoadOrderAsync(orderId, cancellationToken);
            if (order.FulfillmentStatus == OrderFulfillmentStatus.Cancelled) return order;
            if (order.PayPalCaptureId is not null || order.FulfillmentStatus == OrderFulfillmentStatus.Fulfilled)
                throw Conflict("A fulfilled or captured order cannot be cancelled; issue a refund instead.");
            if (order.PayPalAuthorizationId is not null)
            {
                await _payPal.VoidAsync(order.PayPalAuthorizationId, RequestId("void", order.PaymentReference),
                    cancellationToken);
                order.Cancel("VOIDED");
            }
            else
            {
                order.Cancel();
            }
            await _db.SaveChangesAsync(cancellationToken);
            return order;
        }, cancellationToken);
    }

    public async Task<PaymentRefund> RefundAsync(int orderId, string buyerId, decimal? requestedAmount,
        string idempotencyKey, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(idempotencyKey) || idempotencyKey.Length > 108)
            throw BadRequest("idempotencyKey is required and must be at most 108 characters.");
        return await WithOrderLock(orderId, async () =>
        {
            var order = await LoadOrderAsync(orderId, cancellationToken);
            EnsureOwner(order, buyerId);
            if (order.PayPalCaptureId is null || !order.CapturedAmount.HasValue)
                throw Conflict("Only a captured payment can be refunded.");

            var existing = order.FindRefund(idempotencyKey);
            if (existing?.PayPalRefundId is not null) return existing;

            var reserved = order.Refunds.Where(x => x.Status != "FAILED").Sum(x => x.Amount);
            var remaining = Money(order.CapturedAmount.Value - reserved);
            if (remaining <= 0) throw Conflict("The captured payment has already been fully refunded.");
            var amount = requestedAmount.HasValue ? Money(requestedAmount.Value) : remaining;
            if (amount <= 0) throw BadRequest("Refund amount must be greater than zero.");
            if (amount > remaining)
                throw Conflict($"Refund amount exceeds the remaining refundable amount of {remaining:0.00} {_options.Currency}.");

            var refund = existing ?? order.BeginRefund(idempotencyKey, amount);
            if (existing is null) await _db.SaveChangesAsync(cancellationToken);
            PayPalRefundResult result;
            try
            {
                result = await _payPal.RefundAsync(order.PayPalCaptureId, amount, _options.Currency,
                    $"eshop-order-{order.Id}", HashedRequestId("refund", order.PayPalCaptureId, idempotencyKey),
                    cancellationToken);
            }
            catch (PayPalApiException ex) when ((int)ex.StatusCode is >= 400 and < 500)
            {
                refund.Fail();
                await _db.SaveChangesAsync(cancellationToken);
                throw;
            }
            EnsureMoneyMatches(amount, result.Amount, result.Currency, "refund");
            refund.Complete(result.Id, result.Status, result.Amount, result.RefundedPayPalFee,
                result.MerchantNetDebit, result.UpdatedAt);
            order.RefreshRefundState();
            await _db.SaveChangesAsync(cancellationToken);
            return refund;
        }, cancellationToken);
    }

    public async Task<PaymentMethod> SavePaymentMethodAsync(string buyerId, CardDetails card,
        CancellationToken cancellationToken)
    {
        ValidateCard(card);
        var merchantCustomerId = MerchantCustomerId(buyerId);
        var result = await _payPal.CreatePaymentTokenAsync(merchantCustomerId, card,
            $"vault-{Guid.NewGuid():N}", cancellationToken);
        var method = new PaymentMethod(buyerId, result.Id, result.CustomerId, result.Brand,
            result.LastDigits, result.Expiry);
        _db.PaymentMethods.Add(method);
        await _db.SaveChangesAsync(cancellationToken);
        return method;
    }

    public async Task DeletePaymentMethodAsync(int paymentMethodId, string buyerId,
        CancellationToken cancellationToken)
    {
        var method = await _db.PaymentMethods.SingleOrDefaultAsync(x =>
            x.Id == paymentMethodId && x.BuyerId == buyerId, cancellationToken);
        if (method is null) throw NotFound("The saved payment method does not exist.");
        if (!method.IsActive) return;
        try
        {
            await _payPal.DeletePaymentTokenAsync(method.PayPalTokenId, cancellationToken);
        }
        catch (PayPalApiException ex) when (ex.StatusCode == HttpStatusCode.NotFound)
        {
            // A previous retry may already have deleted the token at PayPal.
        }
        method.Delete();
        await _db.SaveChangesAsync(cancellationToken);
    }

    private async Task<Order> LoadOrderAsync(int orderId, CancellationToken cancellationToken)
    {
        var order = await _db.Orders.Include(x => x.OrderItems).ThenInclude(x => x.ItemOrdered)
            .Include(x => x.Refunds).SingleOrDefaultAsync(x => x.Id == orderId, cancellationToken);
        return order ?? throw NotFound($"Order {orderId} was not found.");
    }

    private static void EnsureOwner(Order order, string buyerId)
    {
        if (!string.Equals(order.BuyerId, buyerId, StringComparison.Ordinal))
            throw NotFound($"Order {order.Id} was not found.");
    }

    public static void ValidateCard(CardDetails card)
    {
        var number = new string(card.Number.Where(char.IsDigit).ToArray());
        if (string.IsNullOrWhiteSpace(card.Name) || number.Length is < 13 or > 19 ||
            !ExpiryPattern.IsMatch(card.Expiry) || card.SecurityCode.Length is < 3 or > 4 ||
            !card.SecurityCode.All(char.IsDigit))
            throw BadRequest("Card name, number, future expiry (YYYY-MM), and 3-4 digit security code are required.");
        if (!DateTime.TryParseExact(card.Expiry + "-01", "yyyy-MM-dd", CultureInfo.InvariantCulture,
                DateTimeStyles.None, out var expiry) || expiry.AddMonths(1) <= DateTime.UtcNow.Date)
            throw BadRequest("Card expiry must be in the future.");
        if (card.BillingAddress is null || string.IsNullOrWhiteSpace(card.BillingAddress.AddressLine1) ||
            string.IsNullOrWhiteSpace(card.BillingAddress.City) ||
            string.IsNullOrWhiteSpace(card.BillingAddress.PostalCode) ||
            card.BillingAddress.CountryCode.Length != 2)
            throw BadRequest("A complete card billing address with a two-letter country code is required.");
    }

    private static decimal Money(decimal value) => Math.Round(value, 2, MidpointRounding.AwayFromZero);

    private void EnsureMoneyMatches(decimal expected, decimal actual, string currency, string operation)
    {
        if (Money(actual) != Money(expected) ||
            !string.Equals(currency, _options.Currency, StringComparison.OrdinalIgnoreCase))
            throw new PaymentWorkflowException(HttpStatusCode.BadGateway,
                $"PayPal {operation} amount {actual:0.00} {currency} did not match order amount {expected:0.00} {_options.Currency}.");
    }

    private static string RequestId(string operation, string paymentReference) =>
        $"eshop-{operation}-{paymentReference}";

    private static string HashedRequestId(string operation, string resourceId, string callerKey)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes($"{operation}:{resourceId}:{callerKey}"));
        return $"eshop-{operation}-{Convert.ToHexString(bytes).ToLowerInvariant()}";
    }

    private static string MerchantCustomerId(string buyerId)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(buyerId));
        return $"eshop_{Convert.ToHexString(bytes)[..32].ToLowerInvariant()}";
    }

    private static async Task<T> WithOrderLock<T>(int orderId, Func<Task<T>> action,
        CancellationToken cancellationToken)
    {
        var gate = OrderLocks.GetOrAdd(orderId, _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(cancellationToken);
        try { return await action(); }
        finally { gate.Release(); }
    }

    private static PaymentWorkflowException BadRequest(string message) => new(HttpStatusCode.BadRequest, message);
    private static PaymentWorkflowException NotFound(string message) => new(HttpStatusCode.NotFound, message);
    private static PaymentWorkflowException Conflict(string message) => new(HttpStatusCode.Conflict, message);
}
