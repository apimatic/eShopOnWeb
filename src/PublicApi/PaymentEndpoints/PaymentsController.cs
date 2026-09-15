using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.eShopWeb.ApplicationCore.Entities;
using Microsoft.eShopWeb.ApplicationCore.Entities.BuyerAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.Infrastructure.Data;
using Microsoft.eShopWeb.PublicApi.Payments;

namespace Microsoft.eShopWeb.PublicApi.PaymentEndpoints;

[ApiController]
[Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
public sealed class PaymentsController : ControllerBase
{
    private readonly CatalogContext _db;
    private readonly IPayPalClient _payPal;
    private readonly OrderOperationLock _orderLock;

    public PaymentsController(CatalogContext db, IPayPalClient payPal, OrderOperationLock orderLock)
    {
        _db = db;
        _payPal = payPal;
        _orderLock = orderLock;
    }

    [HttpPost("api/orders")]
    public async Task<ActionResult<PlaceOrderResponse>> PlaceOrder(
        [FromBody] PlaceOrderRequest request, CancellationToken cancellationToken)
    {
        if (request.Items is null || request.Items.Count == 0 || request.Items.Any(x => x.Quantity <= 0))
        {
            return BadRequest(new { message = "At least one catalog item with a positive quantity is required." });
        }
        if (request.ShipToAddress is null)
        {
            return BadRequest(new { message = "A shipping address is required." });
        }

        var requested = request.Items
            .GroupBy(x => x.CatalogItemId)
            .ToDictionary(x => x.Key, x => x.Sum(y => y.Quantity));
        var catalogItems = await _db.CatalogItems
            .Where(x => requested.Keys.Contains(x.Id))
            .ToListAsync(cancellationToken);
        var missing = requested.Keys.Except(catalogItems.Select(x => x.Id)).ToArray();
        if (missing.Length > 0)
        {
            return BadRequest(new { message = $"Catalog items do not exist: {string.Join(", ", missing)}." });
        }

        var items = catalogItems.Select(item => new OrderItem(
            new CatalogItemOrdered(item.Id, item.Name, item.PictureUri),
            item.Price, requested[item.Id])).ToList();
        var address = request.ShipToAddress;
        var order = new Order(BuyerId(),
            new Address(address.Street, address.City, address.State, address.Country, address.ZipCode), items);
        _db.Orders.Add(order);
        await _db.SaveChangesAsync(cancellationToken);

        return Created($"/api/orders/{order.Id}", new PlaceOrderResponse(
            order.Id, order.PaymentStatus.ToString(), order.Total(), _payPal.Currency));
    }

    [HttpPost("api/orders/{orderId:int}/pay")]
    public async Task<ActionResult<OrderResponse>> Pay(int orderId,
        [FromBody] PayOrderRequest request, CancellationToken cancellationToken)
    {
        if ((request.Card is null) == (request.PaymentMethodId is null))
        {
            return BadRequest(new { message = "Supply either card or paymentMethodId, but not both." });
        }

        using (await _orderLock.AcquireAsync(orderId, cancellationToken))
        {
            var order = await FindOrder(orderId, BuyerId(), cancellationToken);
            if (order is null) return NotFound();
            if (order.PaymentStatus != PaymentStatus.AwaitingPayment)
            {
                return Ok(ToResponse(order));
            }

            string? vaultId = null;
            if (request.PaymentMethodId is int paymentMethodId)
            {
                var paymentMethod = await _db.PaymentMethods.SingleOrDefaultAsync(
                    x => x.Id == paymentMethodId && x.BuyerId == BuyerId(), cancellationToken);
                if (paymentMethod is null) return NotFound(new { message = "Saved payment method was not found." });
                vaultId = paymentMethod.PayPalVaultId;
            }

            var authorization = await _payPal.AuthorizeAsync(order.Id, order.PaymentReference, order.Total(),
                request.Card is null ? null : ToPayPalCard(request.Card), vaultId,
                $"eshop-{order.PaymentReference}-authorize", cancellationToken);
            if (authorization.Amount != order.Total() ||
                !string.Equals(authorization.Currency, _payPal.Currency, StringComparison.OrdinalIgnoreCase))
            {
                throw new PaymentOperationException(
                    "PayPal authorized an amount or currency that does not match the order.", HttpStatusCode.BadGateway);
            }
            if (!string.Equals(authorization.Status, "CREATED", StringComparison.OrdinalIgnoreCase) &&
                !string.Equals(authorization.Status, "PENDING", StringComparison.OrdinalIgnoreCase))
            {
                throw new PaymentOperationException(
                    $"PayPal authorization is {authorization.Status}; the order was not marked paid.");
            }

            order.RecordAuthorization(authorization.OrderId, authorization.AuthorizationId,
                authorization.Status, authorization.Amount, authorization.Currency,
                authorization.CreatedAt, authorization.ExpiresAt);
            await _db.SaveChangesAsync(cancellationToken);
            return Ok(ToResponse(order));
        }
    }

    [HttpPost("api/orders/{orderId:int}/fulfil")]
    [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS,
        AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
    public async Task<ActionResult<OrderResponse>> Fulfil(int orderId, CancellationToken cancellationToken)
    {
        using (await _orderLock.AcquireAsync(orderId, cancellationToken))
        {
            var order = await FindOrder(orderId, null, cancellationToken);
            if (order is null) return NotFound();
            if (order.FulfilmentStatus == FulfilmentStatus.Fulfilled) return Ok(ToResponse(order));
            if (order.PaymentStatus is not (PaymentStatus.Authorized or PaymentStatus.CapturePending))
            {
                return Conflict(new { message = "The order must have an authorization before it can be fulfilled." });
            }

            var payment = order.Payment!;
            PayPalCapture capture;
            if (order.PaymentStatus == PaymentStatus.CapturePending)
            {
                capture = await _payPal.GetCaptureAsync(payment.CaptureId!, cancellationToken);
            }
            else
            {
                if (DateTimeOffset.UtcNow >= payment.AuthorizationHonorExpiresAt)
                {
                    if (DateTimeOffset.UtcNow >= payment.OriginalAuthorizedAt.AddDays(30))
                    {
                        throw new PaymentOperationException(
                            "The PayPal authorization is over 30 days old and cannot be renewed. Ask the shopper to pay again.");
                    }

                    try
                    {
                        var renewed = await _payPal.ReauthorizeAsync(payment.AuthorizationId,
                            order.Total(), $"eshop-{order.PaymentReference}-reauthorize-{payment.ReauthorizationCount + 1}",
                            cancellationToken);
                        if (!string.Equals(renewed.Status, "CREATED", StringComparison.OrdinalIgnoreCase))
                        {
                            throw new PaymentOperationException(
                                $"PayPal returned {renewed.Status} while renewing the authorization. Ask the shopper to pay again.");
                        }
                        order.RecordReauthorization(renewed.AuthorizationId, renewed.Status,
                            renewed.CreatedAt, renewed.ExpiresAt);
                        await _db.SaveChangesAsync(cancellationToken);
                        payment = order.Payment!;
                    }
                    catch (PayPalApiException ex) when (ex.StatusCode is HttpStatusCode.NotFound or HttpStatusCode.UnprocessableEntity)
                    {
                        throw new PaymentOperationException(
                            "PayPal can no longer renew this authorization. Ask the shopper to pay again.");
                    }
                }

                capture = await _payPal.CaptureAsync(payment.AuthorizationId, order.Total(),
                    $"eshop-{order.PaymentReference}-capture", cancellationToken);
            }

            if (string.Equals(capture.Status, "PENDING", StringComparison.OrdinalIgnoreCase))
            {
                order.RecordCapturePending(capture.CaptureId, capture.Status, capture.Amount);
                await _db.SaveChangesAsync(cancellationToken);
                throw new PaymentOperationException(
                    "PayPal reports the capture as pending. Retry fulfilment after the payment completes.");
            }
            if (!string.Equals(capture.Status, "COMPLETED", StringComparison.OrdinalIgnoreCase))
            {
                throw new PaymentOperationException(
                    $"PayPal reports the capture as {capture.Status}; the order was not fulfilled.");
            }
            if (capture.PayPalFee is null || capture.NetAmount is null)
            {
                throw new PaymentOperationException(
                    "PayPal completed the capture without returning fee and net proceeds.", HttpStatusCode.BadGateway);
            }

            order.MarkFulfilled(capture.CaptureId, capture.Status, capture.Amount,
                capture.PayPalFee.Value, capture.NetAmount.Value, capture.CreatedAt);
            await _db.SaveChangesAsync(cancellationToken);
            return Ok(ToResponse(order));
        }
    }

    [HttpPost("api/orders/{orderId:int}/cancel")]
    [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS,
        AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
    public async Task<ActionResult<OrderResponse>> Cancel(int orderId, CancellationToken cancellationToken)
    {
        using (await _orderLock.AcquireAsync(orderId, cancellationToken))
        {
            var order = await FindOrder(orderId, null, cancellationToken);
            if (order is null) return NotFound();
            if (order.PaymentStatus == PaymentStatus.Cancelled) return Ok(ToResponse(order));
            if (order.PaymentStatus != PaymentStatus.Authorized)
            {
                return Conflict(new { message = "Only an authorized, unfulfilled order can be cancelled." });
            }

            var status = await _payPal.VoidAsync(order.Payment!.AuthorizationId,
                $"eshop-{order.PaymentReference}-void", cancellationToken);
            order.MarkCancelled(status, DateTimeOffset.UtcNow);
            await _db.SaveChangesAsync(cancellationToken);
            return Ok(ToResponse(order));
        }
    }

    [HttpPost("api/orders/{orderId:int}/refunds")]
    public async Task<ActionResult<RefundCreatedResponse>> Refund(int orderId,
        [FromBody] RefundOrderRequest request, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.IdempotencyKey) || request.IdempotencyKey.Length > 108)
        {
            return BadRequest(new { message = "idempotencyKey is required and may contain at most 108 characters." });
        }

        using (await _orderLock.AcquireAsync(orderId, cancellationToken))
        {
            var order = await FindOrder(orderId, BuyerId(), cancellationToken);
            if (order is null) return NotFound();
            var existing = order.Payment?.Refunds.SingleOrDefault(x => x.IdempotencyKey == request.IdempotencyKey);
            if (existing is not null)
            {
                return Ok(new RefundCreatedResponse(existing.PayPalRefundId, existing.Status,
                    existing.Amount, order.Payment!.CapturedAmount!.Value - order.Payment.RefundedAmount));
            }
            if (order.FulfilmentStatus != FulfilmentStatus.Fulfilled || order.Payment?.CaptureId is null)
            {
                return Conflict(new { message = "Only a fulfilled order with a completed capture can be refunded." });
            }

            var remaining = order.Payment.CapturedAmount!.Value - order.Payment.RefundedAmount;
            var amount = request.Amount ?? remaining;
            if (amount <= 0 || amount > remaining)
            {
                return BadRequest(new { message = $"Refund amount must be positive and no more than {remaining:0.00}." });
            }

            var refund = await _payPal.RefundAsync(order.Payment.CaptureId, amount,
                request.IdempotencyKey, cancellationToken);
            order.RecordRefund(request.IdempotencyKey, refund.RefundId, refund.Status,
                refund.Amount, refund.CreatedAt);
            await _db.SaveChangesAsync(cancellationToken);
            return StatusCode((int)HttpStatusCode.Created, new RefundCreatedResponse(
                refund.RefundId, refund.Status, refund.Amount,
                order.Payment.CapturedAmount.Value - order.Payment.RefundedAmount));
        }
    }

    [HttpGet("api/my-orders")]
    public async Task<ActionResult<IReadOnlyList<OrderResponse>>> MyOrders(CancellationToken cancellationToken)
    {
        var orders = await OrdersWithPayment()
            .Where(x => x.BuyerId == BuyerId())
            .OrderByDescending(x => x.OrderDate)
            .ToListAsync(cancellationToken);
        return Ok(orders.Select(ToResponse).ToList());
    }

    [HttpPost("api/payment-methods")]
    public async Task<ActionResult<PaymentMethodResponse>> SavePaymentMethod(
        [FromBody] SavePaymentMethodRequest request, CancellationToken cancellationToken)
    {
        if (request.Card is null) return BadRequest(new { message = "Card is required." });
        var buyerId = BuyerId();
        var vaulted = await _payPal.VaultCardAsync(buyerId, ToPayPalCard(request.Card),
            $"eshop-vault-{Guid.NewGuid():N}", cancellationToken);
        var paymentMethod = new PaymentMethod(buyerId, vaulted.VaultId,
            vaulted.Brand, vaulted.Last4, vaulted.Expiry, request.Alias);
        _db.PaymentMethods.Add(paymentMethod);
        await _db.SaveChangesAsync(cancellationToken);
        var response = ToResponse(paymentMethod);
        return Created($"/api/payment-methods/{paymentMethod.Id}", response);
    }

    [HttpGet("api/payment-methods")]
    public async Task<ActionResult<IReadOnlyList<PaymentMethodResponse>>> PaymentMethods(
        CancellationToken cancellationToken)
    {
        var methods = await _db.PaymentMethods.AsNoTracking()
            .Where(x => x.BuyerId == BuyerId())
            .OrderByDescending(x => x.CreatedAt)
            .ToListAsync(cancellationToken);
        return Ok(methods.Select(ToResponse).ToList());
    }

    [HttpDelete("api/payment-methods/{paymentMethodId:int}")]
    public async Task<IActionResult> DeletePaymentMethod(int paymentMethodId,
        CancellationToken cancellationToken)
    {
        var method = await _db.PaymentMethods.SingleOrDefaultAsync(
            x => x.Id == paymentMethodId && x.BuyerId == BuyerId(), cancellationToken);
        if (method is null) return NotFound();
        try
        {
            await _payPal.DeleteVaultedCardAsync(method.PayPalVaultId, cancellationToken);
        }
        catch (PayPalApiException ex) when (ex.StatusCode == HttpStatusCode.NotFound)
        {
            // The desired state already exists remotely; remove the stale local reference.
        }
        _db.PaymentMethods.Remove(method);
        await _db.SaveChangesAsync(cancellationToken);
        return NoContent();
    }

    [HttpGet("api/reconciliation")]
    [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS,
        AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
    public async Task<ActionResult<ReconciliationResponse>> Reconciliation(
        [FromQuery] DateTimeOffset from, [FromQuery] DateTimeOffset to,
        CancellationToken cancellationToken)
    {
        if (to <= from) return BadRequest(new { message = "to must be later than from." });
        var transactions = await _payPal.ListTransactionsAsync(from, to, cancellationToken);
        var transactionIds = transactions.Select(x => x.TransactionId)
            .Concat(transactions.Where(x => x.PayPalReferenceId is not null)
                .Select(x => x.PayPalReferenceId!))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        var paymentReferences = transactions
            .SelectMany(x => new[] { x.CustomField, x.InvoiceId?.StartsWith("eshop-", StringComparison.OrdinalIgnoreCase) == true
                ? x.InvoiceId[6..]
                : null })
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Select(x => x!)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        var orders = await OrdersWithPayment().AsNoTracking()
            .Where(x => (x.OrderDate >= from && x.OrderDate <= to) ||
                paymentReferences.Contains(x.PaymentReference) ||
                (x.Payment != null &&
                    (transactionIds.Contains(x.Payment.PayPalOrderId) ||
                     transactionIds.Contains(x.Payment.AuthorizationId) ||
                     (x.Payment.CaptureId != null && transactionIds.Contains(x.Payment.CaptureId)) ||
                     (x.Payment.AuthorizedAt >= from && x.Payment.AuthorizedAt <= to) ||
                     (x.Payment.CapturedAt >= from && x.Payment.CapturedAt <= to) ||
                     x.Payment.Refunds.Any(r =>
                         transactionIds.Contains(r.PayPalRefundId) ||
                         (r.CreatedAt >= from && r.CreatedAt <= to)))))
            .ToListAsync(cancellationToken);

        var used = new HashSet<string>(StringComparer.Ordinal);
        var entries = new List<ReconciliationEntryResponse>();
        foreach (var order in orders)
        {
            var invoiceId = $"eshop-{order.PaymentReference}";
            var matches = transactions.Where(x =>
                string.Equals(x.InvoiceId, invoiceId, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(x.CustomField, order.PaymentReference, StringComparison.Ordinal) ||
                string.Equals(x.TransactionId, order.Payment?.CaptureId, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(x.TransactionId, order.Payment?.AuthorizationId, StringComparison.OrdinalIgnoreCase) ||
                (order.Payment?.Refunds.Any(r => string.Equals(x.TransactionId,
                    r.PayPalRefundId, StringComparison.OrdinalIgnoreCase)) ?? false) ||
                string.Equals(x.PayPalReferenceId, order.Payment?.PayPalOrderId, StringComparison.OrdinalIgnoreCase))
                .ToList();
            foreach (var match in matches) used.Add(TransactionKey(match));
            entries.Add(new ReconciliationEntryResponse(order.Id,
                matches.Count == 0 && order.Payment is not null ? "MissingFromPayPal" : "Matched",
                order.Total(), order.PaymentStatus.ToString(), order.Payment?.CaptureId,
                matches.Select(ToResponse).ToList()));
        }

        entries.AddRange(transactions.Where(x => !used.Contains(TransactionKey(x)))
            .Select(x => new ReconciliationEntryResponse(null, "MissingFromEShop", null,
                null, null, new[] { ToResponse(x) })));
        return Ok(new ReconciliationResponse(from, to, transactions.Count, entries));
    }

    private IQueryable<Order> OrdersWithPayment() => _db.Orders
        .Include(x => x.OrderItems)
        .Include(x => x.Payment)
        .ThenInclude(x => x!.Refunds);

    private Task<Order?> FindOrder(int orderId, string? buyerId, CancellationToken cancellationToken) =>
        OrdersWithPayment().SingleOrDefaultAsync(x => x.Id == orderId &&
            (buyerId == null || x.BuyerId == buyerId), cancellationToken);

    private string BuyerId() => User.Identity?.Name
        ?? throw new UnauthorizedAccessException("The token does not contain a shopper identity.");

    private static PayPalCard ToPayPalCard(CardRequest card) => new(card.Name, card.Number,
        card.Expiry, card.SecurityCode, card.CountryCode, card.AddressLine1, card.AddressLine2,
        card.City, card.State, card.PostalCode);

    private static PaymentMethodResponse ToResponse(PaymentMethod method) => new(
        method.Id, method.Brand, method.Last4, method.Expiry, method.Alias);

    private static OrderResponse ToResponse(Order order)
    {
        var payment = order.Payment;
        return new OrderResponse(order.Id, order.OrderDate, order.Total(),
            order.PaymentStatus.ToString(), order.FulfilmentStatus.ToString(),
            order.OrderItems.Select(x => new OrderItemResponse(x.ItemOrdered.CatalogItemId,
                x.ItemOrdered.ProductName, x.UnitPrice, x.Units, x.UnitPrice * x.Units)).ToList(),
            new PaymentResponse(order.PaymentStatus.ToString(), payment?.PayPalOrderId,
                payment?.AuthorizationId, payment?.AuthorizationStatus, payment?.AuthorizedAmount,
                payment?.Currency, payment?.AuthorizationExpiresAt, payment?.CaptureId,
                payment?.CaptureStatus, payment?.CapturedAmount, payment?.PayPalFee, payment?.NetAmount,
                payment?.RefundedAmount ?? 0,
                payment?.Refunds.Select(x => new RefundResponse(x.PayPalRefundId, x.Status,
                    x.Amount, x.CreatedAt, x.IdempotencyKey)).ToList() ?? new List<RefundResponse>()));
    }

    private static ReconciliationTransactionResponse ToResponse(PayPalTransaction transaction) => new(
        transaction.TransactionId, transaction.PayPalReferenceId, transaction.EventCode,
        transaction.Status, transaction.InitiatedAt, transaction.Amount, transaction.Currency,
        transaction.Fee, transaction.InvoiceId, transaction.CustomField);

    private static string TransactionKey(PayPalTransaction transaction) =>
        $"{transaction.TransactionId}|{transaction.EventCode}|{transaction.InitiatedAt:O}|{transaction.Amount}";
}
