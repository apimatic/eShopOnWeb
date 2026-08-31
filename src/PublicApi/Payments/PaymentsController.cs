using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Microsoft.eShopWeb.ApplicationCore.Entities.BuyerAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.Infrastructure.Data;

namespace Microsoft.eShopWeb.PublicApi.Payments;

[ApiController]
[Route("api")]
[Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
public sealed class PaymentsController : ControllerBase
{
    private static readonly ConcurrentDictionary<int, SemaphoreSlim> OrderLocks = new();
    private readonly CatalogContext _db;
    private readonly IPayPalClient _payPal;
    private readonly PayPalOptions _options;

    public PaymentsController(CatalogContext db, IPayPalClient payPal, IOptions<PayPalOptions> options)
    {
        _db = db;
        _payPal = payPal;
        _options = options.Value;
    }

    [HttpPost("orders")]
    public async Task<ActionResult<CreateOrderResponse>> CreateOrder(CreateOrderRequest request,
        CancellationToken cancellationToken)
    {
        if (request.Items is null || request.Items.Count == 0 || request.Items.Any(x => x.CatalogItemId <= 0 || x.Quantity <= 0))
            return BadRequest(ProblemBody("invalid_items", "At least one catalog item with a positive quantity is required."));
        var requested = request.Items.GroupBy(x => x.CatalogItemId)
            .ToDictionary(x => x.Key, x => x.Sum(y => y.Quantity));
        var catalogItems = await _db.CatalogItems.Where(x => requested.Keys.Contains(x.Id))
            .ToListAsync(cancellationToken);
        if (catalogItems.Count != requested.Count)
            return BadRequest(ProblemBody("catalog_item_not_found", "One or more catalog items do not exist."));
        var items = catalogItems.Select(item => new OrderItem(
            new CatalogItemOrdered(item.Id, item.Name, item.PictureUri), item.Price, requested[item.Id])).ToList();
        var address = request.ShippingAddress ?? new ShippingAddressRequest("Not supplied", "Not supplied", "",
            "Not supplied", "00000");
        var order = new Order(ShopperId(), new Address(address.Street, address.City, address.State,
            address.Country, address.ZipCode), items);
        _db.Orders.Add(order);
        await _db.SaveChangesAsync(cancellationToken);
        return Created($"/api/orders/{order.Id}", new CreateOrderResponse(order.Id, order.Total(), _options.Currency,
            order.PaymentState.ToString()));
    }

    [HttpPost("orders/{orderId:int}/pay")]
    public async Task<ActionResult<OrderPaymentResponse>> Pay(int orderId, PayOrderRequest request,
        CancellationToken cancellationToken)
    {
        if ((request.Card is null) == (request.PaymentMethodId is null))
            return BadRequest(ProblemBody("invalid_payment_source", "Supply either card or paymentMethodId, but not both."));
        var gate = OrderLocks.GetOrAdd(orderId, _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(cancellationToken);
        try
        {
            var order = await OwnedOrder(orderId, cancellationToken);
            if (order is null) return NotFound();
            if (order.PaymentState == PaymentState.Authorized) return PaymentResponse(order);
            if (order.PaymentState != PaymentState.AwaitingPayment)
                return Conflict(ProblemBody("invalid_order_state", $"Order is {order.PaymentState} and cannot be paid."));

            CardInput? card = null;
            string? vaultId = null;
            if (request.PaymentMethodId.HasValue)
            {
                var method = await _db.PaymentMethods.SingleOrDefaultAsync(x => x.Id == request.PaymentMethodId &&
                    x.OwnerId == ShopperId() && !x.IsDeleted, cancellationToken);
                if (method is null) return NotFound(ProblemBody("payment_method_not_found", "The saved card does not exist."));
                vaultId = method.CardId;
            }
            else
            {
                var validation = ValidateCard(request.Card!);
                if (validation is not null) return BadRequest(ProblemBody("invalid_card", validation));
                card = request.Card!.ToPayPal();
            }

            try
            {
                var result = await _payPal.AuthorizeAsync(order.PaymentReference, order.Total(), card, vaultId,
                    StableId("pay", order.PaymentReference), cancellationToken);
                order.RecordAuthorization(result.OrderId, result.AuthorizationId, result.Status,
                    _options.Currency, result.CreateTime, result.ExpirationTime);
                await _db.SaveChangesAsync(cancellationToken);
                return PaymentResponse(order);
            }
            catch (Exception exception) when (exception is PayPalApiException or PayPalPayerActionRequiredException)
            {
                return PayPalProblem(exception);
            }
        }
        finally { gate.Release(); }
    }

    [HttpPost("orders/{orderId:int}/fulfil")]
    [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS,
        AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
    public async Task<ActionResult<OrderPaymentResponse>> Fulfil(int orderId, CancellationToken cancellationToken)
    {
        var gate = OrderLocks.GetOrAdd(orderId, _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(cancellationToken);
        try
        {
            var order = await _db.Orders.Include(x => x.OrderItems).SingleOrDefaultAsync(x => x.Id == orderId, cancellationToken);
            if (order is null) return NotFound();
            if (order.PaymentState is PaymentState.Captured or PaymentState.PartiallyRefunded or PaymentState.Refunded)
                return PaymentResponse(order);
            if (order.PaymentState != PaymentState.Authorized || order.PayPalAuthorizationId is null)
                return Conflict(ProblemBody("order_not_authorized", "Authorize the order before fulfilment."));

            try
            {
                var now = DateTimeOffset.UtcNow;
                if (order.AuthorizationExpiresAt.HasValue && now >= order.AuthorizationExpiresAt.Value)
                    return Conflict(ProblemBody("authorization_expired",
                        "The PayPal authorization is outside its renewal window. Ask the shopper to authorize the order again with a payment method."));
                if (order.AuthorizationCreatedAt.HasValue && now >= order.AuthorizationCreatedAt.Value.AddDays(3))
                {
                    try
                    {
                        var renewed = await _payPal.ReauthorizeAsync(order.PayPalAuthorizationId, order.Total(),
                            order.PaymentCurrency!, StableId("renew", order.PayPalAuthorizationId), cancellationToken);
                        order.RecordReauthorization(renewed.AuthorizationId, renewed.Status, renewed.CreateTime,
                            renewed.ExpirationTime ?? order.AuthorizationExpiresAt);
                        await _db.SaveChangesAsync(cancellationToken);
                    }
                    catch (PayPalApiException exception) when (exception.StatusCode is >= 400 and < 500)
                    {
                        return Conflict(ProblemBody("authorization_cannot_be_renewed",
                            $"PayPal can no longer renew this authorization ({exception.Issue ?? exception.Name}). Ask the shopper to authorize the order again.",
                            exception.DebugId));
                    }
                }
                var captured = await _payPal.CaptureAsync(order.PayPalAuthorizationId!, order.Total(),
                    order.PaymentCurrency!, StableId("capture", order.PayPalAuthorizationId!), cancellationToken);
                order.RecordCapture(captured.Id, captured.Status, captured.GrossAmount, captured.Fee, captured.Net);
                await _db.SaveChangesAsync(cancellationToken);
                return PaymentResponse(order);
            }
            catch (PayPalApiException exception) when (exception.Issue is "AUTHORIZATION_EXPIRED" or "AUTHORIZATION_VOIDED")
            {
                return Conflict(ProblemBody("authorization_cannot_be_renewed",
                    "PayPal can no longer renew this authorization. Ask the shopper to authorize the order again.", exception.DebugId));
            }
            catch (PayPalApiException exception) { return PayPalProblem(exception); }
        }
        finally { gate.Release(); }
    }

    [HttpPost("orders/{orderId:int}/cancel")]
    [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS,
        AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
    public async Task<ActionResult<OrderPaymentResponse>> Cancel(int orderId, CancellationToken cancellationToken)
    {
        var gate = OrderLocks.GetOrAdd(orderId, _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(cancellationToken);
        try
        {
            var order = await _db.Orders.Include(x => x.OrderItems).SingleOrDefaultAsync(x => x.Id == orderId, cancellationToken);
            if (order is null) return NotFound();
            if (order.PaymentState == PaymentState.Cancelled) return PaymentResponse(order);
            if (order.PaymentState is PaymentState.Captured or PaymentState.PartiallyRefunded or PaymentState.Refunded)
                return Conflict(ProblemBody("already_captured", "Captured orders must be refunded, not cancelled."));
            try
            {
                if (order.PayPalAuthorizationId is not null)
                    await _payPal.VoidAsync(order.PayPalAuthorizationId, StableId("void", order.PayPalAuthorizationId), cancellationToken);
                order.RecordCancellation(order.PayPalAuthorizationId is null ? "NOT_CREATED" : "VOIDED");
                await _db.SaveChangesAsync(cancellationToken);
                return PaymentResponse(order);
            }
            catch (PayPalApiException exception) { return PayPalProblem(exception); }
        }
        finally { gate.Release(); }
    }

    [HttpPost("orders/{orderId:int}/refunds")]
    public async Task<ActionResult<RefundResponse>> Refund(int orderId, RefundRequest request,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.IdempotencyKey) || request.IdempotencyKey.Length > 100)
            return BadRequest(ProblemBody("invalid_idempotency_key", "idempotencyKey is required and must be at most 100 characters."));
        var gate = OrderLocks.GetOrAdd(orderId, _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(cancellationToken);
        try
        {
            var order = await OwnedOrder(orderId, cancellationToken);
            if (order is null) return NotFound();
            var previous = await _db.PaymentRefunds.SingleOrDefaultAsync(x => x.OrderId == orderId &&
                x.IdempotencyKey == request.IdempotencyKey, cancellationToken);
            if (previous is not null) return RefundResponse(previous);
            if (order.PaymentState is not (PaymentState.Captured or PaymentState.PartiallyRefunded) ||
                order.PayPalCaptureId is null || !order.CapturedAmount.HasValue)
                return Conflict(ProblemBody("order_not_refundable", "The order has no captured funds available to refund."));
            var remaining = order.CapturedAmount.Value - order.RefundedAmount;
            var amount = request.Amount ?? remaining;
            if (amount <= 0 || amount > remaining)
                return BadRequest(ProblemBody("invalid_refund_amount", $"Refund amount must be greater than zero and no more than {remaining:0.00}."));
            try
            {
                var result = await _payPal.RefundAsync(order.PayPalCaptureId, amount, order.PaymentCurrency!,
                    StableId("refund", order.PayPalCaptureId + ":" + request.IdempotencyKey), cancellationToken);
                var refund = new PaymentRefund(order.Id, request.IdempotencyKey, result.Id, result.Status,
                    result.Amount, result.Currency);
                order.RecordRefund(result.Amount);
                _db.PaymentRefunds.Add(refund);
                await _db.SaveChangesAsync(cancellationToken);
                return RefundResponse(refund);
            }
            catch (PayPalApiException exception) { return PayPalProblem(exception); }
        }
        finally { gate.Release(); }
    }

    [HttpGet("my-orders")]
    public async Task<ActionResult<IReadOnlyList<OrderSummaryResponse>>> MyOrders(CancellationToken cancellationToken)
    {
        var orders = await _db.Orders.AsNoTracking().Include(x => x.OrderItems)
            .Where(x => x.BuyerId == ShopperId()).OrderByDescending(x => x.OrderDate).ToListAsync(cancellationToken);
        return orders.Select(OrderSummary).ToList();
    }

    [HttpPost("payment-methods")]
    public async Task<ActionResult<CreatePaymentMethodResponse>> SavePaymentMethod(SavePaymentMethodRequest request,
        [FromHeader(Name = "Idempotency-Key")] string? idempotencyKey, CancellationToken cancellationToken)
    {
        if (request.Card is null) return BadRequest(ProblemBody("invalid_card", "card is required."));
        var validation = ValidateCard(request.Card);
        if (validation is not null) return BadRequest(ProblemBody("invalid_card", validation));
        var requestId = StableId("vault", ShopperId() + ":" + (idempotencyKey ?? Guid.NewGuid().ToString("N")));
        try
        {
            var result = await _payPal.SaveCardAsync(request.Card.ToPayPal(), requestId, cancellationToken);
            var existing = await _db.PaymentMethods.SingleOrDefaultAsync(x => x.CardId == result.TokenId, cancellationToken);
            if (existing is not null && existing.OwnerId == ShopperId())
                return Created($"/api/payment-methods/{existing.Id}", MethodCreated(existing));
            var method = new PaymentMethod(ShopperId(), result.TokenId, result.Last4, result.Brand,
                result.Expiry, result.CustomerId, request.Alias);
            _db.PaymentMethods.Add(method);
            await _db.SaveChangesAsync(cancellationToken);
            return Created($"/api/payment-methods/{method.Id}", MethodCreated(method));
        }
        catch (Exception exception) when (exception is PayPalApiException or PayPalPayerActionRequiredException)
        {
            return PayPalProblem(exception);
        }
    }

    [HttpGet("payment-methods")]
    public async Task<ActionResult<IReadOnlyList<PaymentMethodResponse>>> PaymentMethods(CancellationToken cancellationToken)
    {
        var methods = await _db.PaymentMethods.AsNoTracking().Where(x => x.OwnerId == ShopperId() && !x.IsDeleted)
            .OrderBy(x => x.Id).ToListAsync(cancellationToken);
        return methods.Select(MethodResponse).ToList();
    }

    [HttpDelete("payment-methods/{paymentMethodId:int}")]
    public async Task<IActionResult> DeletePaymentMethod(int paymentMethodId, CancellationToken cancellationToken)
    {
        var method = await _db.PaymentMethods.SingleOrDefaultAsync(x => x.Id == paymentMethodId &&
            x.OwnerId == ShopperId() && !x.IsDeleted, cancellationToken);
        if (method is null) return NotFound();
        try
        {
            await _payPal.DeletePaymentTokenAsync(method.CardId!, cancellationToken);
            method.Delete();
            await _db.SaveChangesAsync(cancellationToken);
            return NoContent();
        }
        catch (PayPalApiException exception) { return PayPalProblem(exception); }
    }

    [HttpGet("reconciliation")]
    [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS,
        AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
    public async Task<ActionResult<ReconciliationResponse>> Reconciliation([FromQuery] DateTimeOffset from,
        [FromQuery] DateTimeOffset to, CancellationToken cancellationToken)
    {
        if (from == default || to == default || from >= to)
            return BadRequest(ProblemBody("invalid_range", "from and to must be valid ISO-8601 date-times and from must precede to."));
        try
        {
            var paypal = await _payPal.ListTransactionsAsync(from, to, cancellationToken);
            // Load all payment-bearing orders so transactions for an older order (for example, a later
            // capture or refund) still match. The date filter is applied only when finding eShop-only activity.
            var orders = await _db.Orders.AsNoTracking()
                .Where(x => x.PayPalOrderId != null || (x.OrderDate >= from && x.OrderDate <= to))
                .ToListAsync(cancellationToken);
            var refunds = await _db.PaymentRefunds.AsNoTracking().ToListAsync(cancellationToken);
            var rows = paypal.Select(tx =>
            {
                var order = MatchOrder(tx, orders, refunds);
                return new ReconciliationRow(tx.TransactionId, tx.InvoiceId, tx.EventCode, tx.Status,
                    tx.Amount, tx.Currency, tx.InitiatedAt, order?.Id, order?.PaymentState.ToString(),
                    order is null ? "PAYPAL_ONLY" : "MATCHED");
            }).ToList();
            var matchedIds = rows.Where(x => x.OrderId.HasValue).Select(x => x.OrderId!.Value).ToHashSet();
            var refundActivityOrderIds = refunds.Where(x => x.CreatedAt >= from && x.CreatedAt <= to)
                .Select(x => x.OrderId).ToHashSet();
            var localOnly = orders.Where(x => !matchedIds.Contains(x.Id) &&
                ((x.OrderDate >= from && x.OrderDate <= to) ||
                 (x.AuthorizationCreatedAt >= from && x.AuthorizationCreatedAt <= to) ||
                 (x.FulfilledAt >= from && x.FulfilledAt <= to) ||
                 (x.CancelledAt >= from && x.CancelledAt <= to) || refundActivityOrderIds.Contains(x.Id))).Select(x =>
                new ReconciliationLocalOnly(x.Id, x.PaymentState.ToString(), x.Total(),
                    x.PaymentCurrency ?? _options.Currency, x.PayPalOrderId, x.PayPalAuthorizationId,
                    x.PayPalCaptureId)).ToList();
            return new ReconciliationResponse(from, to, rows, localOnly);
        }
        catch (PayPalApiException exception) { return PayPalProblem(exception); }
    }

    private async Task<Order?> OwnedOrder(int orderId, CancellationToken cancellationToken) =>
        await _db.Orders.Include(x => x.OrderItems).SingleOrDefaultAsync(x => x.Id == orderId &&
            x.BuyerId == ShopperId(), cancellationToken);

    private string ShopperId() => User.FindFirstValue(ClaimTypes.Name)
        ?? throw new InvalidOperationException("Authenticated token has no name claim.");

    private static Order? MatchOrder(PayPalTransaction transaction, IReadOnlyList<Order> orders,
        IReadOnlyList<PaymentRefund> refunds)
    {
        if (transaction.InvoiceId?.StartsWith("eshop-", StringComparison.Ordinal) == true)
            return orders.SingleOrDefault(x => x.PaymentReference == transaction.InvoiceId[6..]);
        var order = orders.SingleOrDefault(x => transaction.TransactionId == x.PayPalAuthorizationId ||
            transaction.TransactionId == x.PayPalCaptureId || transaction.TransactionId == x.PayPalOrderId);
        if (order is not null) return order;
        var refund = refunds.SingleOrDefault(x => x.PayPalRefundId == transaction.TransactionId);
        return refund is null ? null : orders.SingleOrDefault(x => x.Id == refund.OrderId);
    }

    private static string StableId(string operation, string value)
    {
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(operation + ":" + value))).ToLowerInvariant();
        return operation[..Math.Min(operation.Length, 6)] + "-" + hash[..24];
    }

    private static string? ValidateCard(CardRequest card)
    {
        var number = card.Number.Replace(" ", string.Empty, StringComparison.Ordinal);
        if (number.Length is < 12 or > 19 || number.Any(x => !char.IsDigit(x))) return "number is invalid.";
        if (!System.Text.RegularExpressions.Regex.IsMatch(card.Expiry, @"^\d{4}-(0[1-9]|1[0-2])$")) return "expiry must use YYYY-MM.";
        if (card.SecurityCode.Length is < 3 or > 4 || card.SecurityCode.Any(x => !char.IsDigit(x))) return "securityCode is invalid.";
        if (string.IsNullOrWhiteSpace(card.Name) || string.IsNullOrWhiteSpace(card.BillingAddress.AddressLine1) ||
            string.IsNullOrWhiteSpace(card.BillingAddress.CountryCode)) return "name and billing address are required.";
        return null;
    }

    private ActionResult PayPalProblem(Exception exception)
    {
        if (exception is PayPalPayerActionRequiredException challenge)
            return UnprocessableEntity(ProblemBody("payer_action_required", challenge.Message));
        var api = (PayPalApiException)exception;
        var status = api.StatusCode is >= 400 and < 500 ? StatusCodes.Status422UnprocessableEntity : StatusCodes.Status502BadGateway;
        return StatusCode(status, ProblemBody(api.Issue ?? api.Name, api.Message, api.DebugId));
    }

    private static object ProblemBody(string code, string detail, string? paypalDebugId = null) =>
        new { code, detail, paypalDebugId };
    private ActionResult<OrderPaymentResponse> PaymentResponse(Order order) => Ok(new OrderPaymentResponse(order.Id,
        order.PaymentState.ToString(), order.Total(), order.PaymentCurrency ?? _options.Currency,
        order.PayPalAuthorizationId, order.PayPalAuthorizationStatus, order.PayPalCaptureId,
        order.PayPalCaptureStatus, order.CapturedAmount, order.PayPalFee, order.NetProceeds, order.RefundedAmount));
    private static ActionResult<RefundResponse> RefundResponse(PaymentRefund refund) =>
        new RefundResponse(refund.PayPalRefundId, refund.Status, refund.Amount, refund.Currency);
    private static CreatePaymentMethodResponse MethodCreated(PaymentMethod x) =>
        new(x.Id, x.Alias, x.Brand, x.Last4!, x.Expiry);
    private static PaymentMethodResponse MethodResponse(PaymentMethod x) =>
        new(x.Id, x.Alias, x.Brand, x.Last4!, x.Expiry);
    private static OrderSummaryResponse OrderSummary(Order x) => new(x.Id, x.OrderDate, x.Total(),
        x.PaymentCurrency, x.PaymentState.ToString(), x.PayPalAuthorizationStatus, x.PayPalCaptureStatus,
        x.CapturedAmount, x.PayPalFee, x.NetProceeds, x.RefundedAmount);
}

public sealed record OrderLineRequest(int CatalogItemId, int Quantity);
public sealed record ShippingAddressRequest(string Street, string City, string State, string Country, string ZipCode);
public sealed record CreateOrderRequest(IReadOnlyList<OrderLineRequest> Items, ShippingAddressRequest? ShippingAddress);
public sealed record CreateOrderResponse(int OrderId, decimal Total, string Currency, string PaymentState);
public sealed record BillingAddressRequest(string AddressLine1, string? AddressLine2, string AdminArea1,
    string AdminArea2, string PostalCode, string CountryCode);
public sealed record CardRequest(string Number, string Expiry, string SecurityCode, string Name,
    BillingAddressRequest BillingAddress)
{
    public CardInput ToPayPal() => new(Number, Expiry, SecurityCode, Name,
        new CardBillingAddress(BillingAddress.AddressLine1, BillingAddress.AddressLine2,
            BillingAddress.AdminArea1, BillingAddress.AdminArea2, BillingAddress.PostalCode,
            BillingAddress.CountryCode));
}
public sealed record PayOrderRequest(CardRequest? Card, int? PaymentMethodId);
public sealed record OrderPaymentResponse(int OrderId, string PaymentState, decimal OrderTotal, string Currency,
    string? AuthorizationId, string? AuthorizationStatus, string? CaptureId, string? CaptureStatus,
    decimal? CapturedAmount, decimal? PayPalFee, decimal? NetProceeds, decimal RefundedAmount);
public sealed record RefundRequest(decimal? Amount, string IdempotencyKey);
public sealed record RefundResponse(string RefundId, string Status, decimal Amount, string Currency);
public sealed record SavePaymentMethodRequest(CardRequest Card, string? Alias);
public sealed record CreatePaymentMethodResponse(int PaymentMethodId, string? Alias, string Brand, string Last4, string Expiry);
public sealed record PaymentMethodResponse(int PaymentMethodId, string? Alias, string Brand, string Last4, string Expiry);
public sealed record OrderSummaryResponse(int OrderId, DateTimeOffset OrderDate, decimal Total, string? Currency,
    string PaymentState, string? AuthorizationStatus, string? CaptureStatus, decimal? CapturedAmount,
    decimal? PayPalFee, decimal? NetProceeds, decimal RefundedAmount);
public sealed record ReconciliationRow(string TransactionId, string? InvoiceId, string EventCode, string PayPalStatus,
    decimal? Amount, string? Currency, DateTimeOffset? InitiatedAt, int? OrderId, string? EshopPaymentState, string MatchStatus);
public sealed record ReconciliationLocalOnly(int OrderId, string PaymentState, decimal Total, string Currency,
    string? PayPalOrderId, string? AuthorizationId, string? CaptureId);
public sealed record ReconciliationResponse(DateTimeOffset From, DateTimeOffset To,
    IReadOnlyList<ReconciliationRow> PayPalTransactions, IReadOnlyList<ReconciliationLocalOnly> EshopOnlyOrders);
