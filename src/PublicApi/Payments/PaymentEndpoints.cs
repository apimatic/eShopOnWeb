using System.Security.Claims;
using System;
using System.Text.Json;
using System.Collections.Generic;
using System.Linq;
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
using PayPalServerSdk.Models;
using EShopOrder = Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate.Order;
using EShopAddress = Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate.Address;

namespace Microsoft.eShopWeb.PublicApi.Payments;

public sealed record OrderLineRequest(int CatalogItemId, int Quantity);
public sealed record CreateOrderRequest(IReadOnlyList<OrderLineRequest> Items, string? Street, string? City, string? State, string? Country, string? ZipCode);
public sealed record PayOrderRequest(string? CardholderName, string? CardNumber, string? Expiry, string? Cvc, int? PaymentMethodId);
public sealed record SavePaymentMethodRequest(string Name, string Number, string Expiry, string Cvc, string? Alias);
public sealed record RefundOrderRequest(decimal? Amount, string IdempotencyKey);

[ApiController]
[Route("api")]
[Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
public sealed class PaymentEndpoints : ControllerBase
{
    private readonly CatalogContext _db;
    private readonly PayPalGateway _paypal;
    private string OwnerId => User.FindFirstValue(ClaimTypes.Name) ?? throw new UnauthorizedAccessException();

    public PaymentEndpoints(CatalogContext db, PayPalGateway paypal) { _db = db; _paypal = paypal; }

    [HttpPost("orders")]
    public async Task<IActionResult> CreateOrder(CreateOrderRequest request, CancellationToken ct)
    {
        if (request.Items is null || request.Items.Count == 0 || request.Items.Any(x => x.Quantity <= 0)) return BadRequest("At least one positive quantity is required.");
        var ids = request.Items.Select(x => x.CatalogItemId).Distinct().ToArray();
        var catalog = await _db.CatalogItems.Where(x => ids.Contains(x.Id)).ToListAsync(ct);
        if (catalog.Count != ids.Length) return NotFound("One or more catalog items were not found.");
        var items = request.Items.Select(line => { var item = catalog.Single(x => x.Id == line.CatalogItemId); return new OrderItem(new CatalogItemOrdered(item.Id, item.Name, item.PictureUri), item.Price, line.Quantity); }).ToList();
        var order = new EShopOrder(OwnerId, new EShopAddress(request.Street ?? "Not provided", request.City ?? "Not provided", request.State ?? "", request.Country ?? "US", request.ZipCode ?? "00000"), items);
        _db.Orders.Add(order); await _db.SaveChangesAsync(ct);
        return Created($"/api/orders/{order.Id}", new { orderId = order.Id, total = order.Total(), paymentStatus = order.PaymentStatus.ToString() });
    }

    [HttpPost("orders/{orderId:int}/pay")]
    public async Task<IActionResult> Pay(int orderId, PayOrderRequest request, CancellationToken ct)
    {
        var order = await OwnOrder(orderId, ct); if (order is null) return NotFound();
        if (order.PaymentStatus == EShopOrder.OrderPaymentStatus.Authorized) return Ok(ToOrder(order));
        if (order.PaymentStatus is not EShopOrder.OrderPaymentStatus.AwaitingPayment and not EShopOrder.OrderPaymentStatus.AwaitingAuthorization) return Conflict("Order is not awaiting payment.");
        CardRequest? card = null;
        if (request.PaymentMethodId is int methodId)
        {
            var method = await _db.SavedPaymentMethods.SingleOrDefaultAsync(x => x.Id == methodId && x.OwnerId == OwnerId, ct);
            if (method is null) return NotFound("Payment method not found.");
            card = new PayPalServerSdk.Models.CardRequest { VaultId = method.ProviderTokenId };
        }
        else
        {
            if (string.IsNullOrWhiteSpace(request.CardNumber) || string.IsNullOrWhiteSpace(request.Expiry) || string.IsNullOrWhiteSpace(request.Cvc)) return BadRequest("Card details or a saved payment method are required.");
            card = new PayPalServerSdk.Models.CardRequest { Name = request.CardholderName, Number = request.CardNumber, Expiry = request.Expiry, SecurityCode = request.Cvc };
        }
        var providerOrderId = order.PaymentProviderOrderId ?? await _paypal.CreateOrderAsync(order.Id, order.Total(), card!, ct);
        if (order.PaymentProviderOrderId is null) order.SetPaymentOrder(providerOrderId!);
        var auth = await _paypal.AuthorizeAsync(providerOrderId!, card, ct);
        if (string.IsNullOrWhiteSpace(auth.AuthorizationId)) return Conflict("PayPal did not return an authorization.");
        order.SetAuthorization(auth.AuthorizationId); await _db.SaveChangesAsync(ct);
        return Ok(ToOrder(order));
    }

    [HttpPost("orders/{orderId:int}/fulfil")]
    [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS, AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
    public async Task<IActionResult> Fulfil(int orderId, CancellationToken ct)
    {
        var order = await _db.Orders.Include(x => x.OrderItems).ThenInclude(x => x.ItemOrdered).SingleOrDefaultAsync(x => x.Id == orderId, ct);
        if (order is null) return NotFound();
        if (order.FulfilmentStatus == EShopOrder.OrderFulfilmentStatus.Fulfilled) return Ok(ToOrder(order));
        if (string.IsNullOrWhiteSpace(order.AuthorizationId)) return Conflict("The order has no PayPal authorization to fulfil.");
        if (string.IsNullOrWhiteSpace(order.PaymentProviderOrderId)) return Conflict("The order has no PayPal order reference; it cannot be renewed.");
        var authorizationId = await _paypal.RenewIfExpiredAsync(order.PaymentProviderOrderId, order.AuthorizationId, ct);
        if (string.IsNullOrWhiteSpace(authorizationId)) return Conflict("The PayPal authorization expired and could not be renewed. Re-authorize the order before fulfilling it.");
        if (authorizationId != order.AuthorizationId) order.SetAuthorization(authorizationId);
        var capture = await _paypal.CaptureAsync(authorizationId, order.Total(), ct);
        if (capture.CaptureId is null) return Conflict("PayPal did not return a capture.");
        order.SetCaptured(capture.CaptureId, capture.Amount, capture.Fee, capture.Net); await _db.SaveChangesAsync(ct);
        return Ok(ToOrder(order));
    }

    [HttpPost("orders/{orderId:int}/cancel")]
    [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS, AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
    public async Task<IActionResult> Cancel(int orderId, CancellationToken ct)
    {
        var order = await _db.Orders.SingleOrDefaultAsync(x => x.Id == orderId, ct); if (order is null) return NotFound();
        if (order.FulfilmentStatus == EShopOrder.OrderFulfilmentStatus.Fulfilled) return Conflict("A fulfilled order cannot be cancelled; use a refund.");
        if (order.PaymentStatus == EShopOrder.OrderPaymentStatus.Cancelled) return Ok(ToOrder(order));
        if (!string.IsNullOrWhiteSpace(order.AuthorizationId)) await _paypal.VoidAsync(order.AuthorizationId, ct);
        order.Cancel(); await _db.SaveChangesAsync(ct); return Ok(ToOrder(order));
    }

    [HttpPost("orders/{orderId:int}/refunds")]
    public async Task<IActionResult> Refund(int orderId, RefundOrderRequest request, CancellationToken ct)
    {
        var order = await OwnOrder(orderId, ct); if (order is null) return NotFound();
        if (string.IsNullOrWhiteSpace(request.IdempotencyKey)) return BadRequest("IdempotencyKey is required.");
        var prior = await _db.RefundRecords.SingleOrDefaultAsync(x => x.OrderId == orderId && x.IdempotencyKey == request.IdempotencyKey, ct);
        if (prior is not null) return Ok(new { refundId = prior.ProviderRefundId ?? prior.Id.ToString(), amount = prior.Amount, status = prior.Status });
        var amount = request.Amount ?? order.CapturedAmount - order.RefundedAmount;
        if (order.PaymentStatus is not EShopOrder.OrderPaymentStatus.Captured and not EShopOrder.OrderPaymentStatus.PartiallyRefunded || amount <= 0 || order.RefundedAmount + amount > order.CapturedAmount) return Conflict("Refund exceeds the captured amount or the order is not captured.");
        var refund = await _paypal.RefundAsync(order.CaptureId!, amount == order.CapturedAmount && order.RefundedAmount == 0 ? null : amount, request.IdempotencyKey, ct);
        var record = new RefundRecord(order.Id, request.IdempotencyKey, refund.RefundId, refund.Amount, refund.Status ?? "PENDING"); _db.RefundRecords.Add(record); order.AddRefund(refund.Amount); await _db.SaveChangesAsync(ct);
        return Ok(new { refundId = record.ProviderRefundId ?? record.Id.ToString(), providerRefundId = record.ProviderRefundId, amount = record.Amount, status = record.Status });
    }

    [HttpGet("my-orders")]
    public async Task<IActionResult> MyOrders(CancellationToken ct) => Ok((await _db.Orders.Where(x => x.BuyerId == OwnerId).Include(x => x.OrderItems).ToListAsync(ct)).Select(ToOrder));

    [HttpPost("payment-methods")]
    public async Task<IActionResult> SavePaymentMethod(SavePaymentMethodRequest request, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.Number) || string.IsNullOrWhiteSpace(request.Expiry) || string.IsNullOrWhiteSpace(request.Cvc)) return BadRequest("Card details are required.");
        var token = await _paypal.SaveCardAsync(request.Name, request.Number, request.Expiry, request.Cvc, ct);
        if (string.IsNullOrWhiteSpace(token)) return Conflict("PayPal did not return a saved-card identifier.");
        var method = new SavedPaymentMethod(OwnerId, token, request.Number.StartsWith("4") ? "VISA" : "CARD", request.Number[^4..], request.Expiry, request.Alias); _db.SavedPaymentMethods.Add(method); await _db.SaveChangesAsync(ct);
        return Created($"/api/payment-methods/{method.Id}", new { paymentMethodId = method.Id, method.Brand, method.Last4, method.Expiry, method.Alias });
    }

    [HttpGet("payment-methods")]
    public async Task<IActionResult> PaymentMethods(CancellationToken ct) => Ok(await _db.SavedPaymentMethods.Where(x => x.OwnerId == OwnerId).Select(x => new { paymentMethodId = x.Id, x.Brand, x.Last4, x.Expiry, x.Alias }).ToListAsync(ct));

    [HttpDelete("payment-methods/{paymentMethodId:int}")]
    public async Task<IActionResult> DeletePaymentMethod(int paymentMethodId, CancellationToken ct)
    {
        var method = await _db.SavedPaymentMethods.SingleOrDefaultAsync(x => x.Id == paymentMethodId && x.OwnerId == OwnerId, ct); if (method is null) return NotFound();
        await _paypal.DeleteCardAsync(method.ProviderTokenId, ct); _db.SavedPaymentMethods.Remove(method); await _db.SaveChangesAsync(ct); return NoContent();
    }

    [HttpGet("reconciliation")]
    [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS, AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
    public async Task<IActionResult> Reconciliation(DateTimeOffset from, DateTimeOffset to, CancellationToken ct)
    {
        if (from >= to) return BadRequest("from must be before to.");
        var provider = await _paypal.SearchTransactionsAsync(from, to, ct);
        var orders = await _db.Orders.Where(x => x.PaymentProviderOrderId != null && x.OrderDate >= from && x.OrderDate <= to).Select(x => new { x.Id, x.PaymentProviderOrderId, x.CaptureId, x.CapturedAmount, x.PaymentStatus }).ToListAsync(ct);
        var known = orders.SelectMany(x => new[] { x.PaymentProviderOrderId, x.CaptureId }).Where(x => x != null).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var providerIds = provider.Select(x => x.TryGetProperty("transaction_info", out var info) && info.TryGetProperty("transaction_id", out var id) ? id.GetString() : null).Where(x => x != null).ToHashSet(StringComparer.OrdinalIgnoreCase);
        return Ok(new { from, to, providerTransactions = provider, eshopOrders = orders, providerOnly = providerIds.Except(known), eshopOnly = known.Except(providerIds) });
    }

    private Task<EShopOrder?> OwnOrder(int id, CancellationToken ct) => _db.Orders.Include(x => x.OrderItems).ThenInclude(x => x.ItemOrdered).SingleOrDefaultAsync(x => x.Id == id && x.BuyerId == OwnerId, ct);
    private static object ToOrder(EShopOrder x) => new { orderId = x.Id, total = x.Total(), paymentStatus = x.PaymentStatus.ToString(), fulfilmentStatus = x.FulfilmentStatus.ToString(), x.PaymentProviderOrderId, x.AuthorizationId, x.CaptureId, x.CapturedAmount, x.RefundedAmount, x.PayPalFee, x.NetProceeds };
}
