using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net.Http;
using System.Security.Claims;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Microsoft.eShopWeb.ApplicationCore.Entities;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Specifications;
using Microsoft.eShopWeb.Infrastructure.Data;

namespace Microsoft.eShopWeb.PublicApi.Payments;

public static class PaymentEndpoints
{
    public static void MapPaymentEndpoints(this IEndpointRouteBuilder app)
    {
        var shopper = new AuthorizeAttribute { AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme };
        var admin = new AuthorizeAttribute { Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS, AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme };
        app.MapPost("api/orders", CreateOrder).RequireAuthorization(shopper);
        app.MapPost("api/orders/{orderId:int}/pay", Pay).RequireAuthorization(shopper);
        app.MapPost("api/orders/{orderId:int}/fulfil", Fulfil).RequireAuthorization(admin);
        app.MapPost("api/orders/{orderId:int}/cancel", Cancel).RequireAuthorization(admin);
        app.MapPost("api/orders/{orderId:int}/refunds", Refund).RequireAuthorization(shopper);
        app.MapGet("api/my-orders", MyOrders).RequireAuthorization(shopper);
        app.MapPost("api/payment-methods", SaveMethod).RequireAuthorization(shopper);
        app.MapGet("api/payment-methods", ListMethods).RequireAuthorization(shopper);
        app.MapDelete("api/payment-methods/{id:int}", DeleteMethod).RequireAuthorization(shopper);
        app.MapGet("api/reconciliation", Reconciliation).RequireAuthorization(admin);
    }

    static string User(HttpContext c) => c.User.Identity?.Name ?? throw new UnauthorizedAccessException();
    static async Task<IResult> CreateOrder(OrderRequest r, IRepository<CatalogItem> catalog, IRepository<Order> orders, IRepository<Payment> payments, IOptions<PayPalOptions> options, HttpContext http)
    {
        if (r.Items is null || r.Items.Count == 0) return Results.BadRequest("At least one catalog item is required.");
        var items = new List<OrderItem>(); foreach (var line in r.Items)
        { var item = await catalog.GetByIdAsync(line.CatalogItemId); if (item is null || line.Quantity <= 0) return Results.BadRequest("Invalid catalog item or quantity."); items.Add(new OrderItem(new CatalogItemOrdered(item.Id, item.Name, item.PictureUri), item.Price, line.Quantity)); }
        var shipping = r.Shipping ?? new ShippingAddress("Not provided", "Not provided", "", "US", "00000");
        var order = await orders.AddAsync(new Order(User(http), new Address(shipping.Street, shipping.City, shipping.State, shipping.Country, shipping.ZipCode), items));
        await payments.AddAsync(new Payment(order.Id, User(http), order.Total(), options.Value.Currency));
        return Results.Created($"/api/orders/{order.Id}", new { orderId = order.Id, total = order.Total(), currency = options.Value.Currency, paymentState = PaymentStatus.AwaitingAuthorization });
    }
    static async Task<IResult> Pay(int orderId, PayRequest r, IRepository<Order> orders, IRepository<Payment> payments, IRepository<PaymentMethod> methods, PayPalClient paypal, HttpContext http, CancellationToken ct)
    {
        var user = User(http); var order = await orders.GetByIdAsync(orderId); if (order is null || order.BuyerId != user) return Results.NotFound(); var payment = await payments.FirstOrDefaultAsync(new PaymentByOrderSpec(orderId)); if (payment is null) return Results.NotFound();
        if (payment.Status == PaymentStatus.Authorized || payment.Status == PaymentStatus.Captured) return Results.Ok(PaymentDto(payment));
        object source; if (r.PaymentMethodId.HasValue) { var method = await methods.GetByIdAsync(r.PaymentMethodId.Value); if (method is null || method.BuyerId != user || method.IsDeleted) return Results.BadRequest("Payment method is not available."); source = new { card = new { vault_id = method.VaultId } }; }
        else { if (r.Card is null) return Results.BadRequest("Provide card details or paymentMethodId."); source = new { card = new { number = r.Card.Number, expiry = r.Card.Expiry, security_code = r.Card.SecurityCode, name = r.Card.Name, billing_address = new { address_line_1 = r.Card.BillingAddress?.Street, admin_area_2 = r.Card.BillingAddress?.City, admin_area_1 = r.Card.BillingAddress?.State, postal_code = r.Card.BillingAddress?.ZipCode, country_code = r.Card.BillingAddress?.Country } } }; }
        var create = await paypal.SendAsync(HttpMethod.Post, "v2/checkout/orders", new { intent = "AUTHORIZE", payment_source = source, purchase_units = new[] { new { reference_id = order.Id.ToString(CultureInfo.InvariantCulture), invoice_id = $"ESHOP-{order.Id}", amount = new { currency_code = payment.Currency, value = payment.Amount.ToString("0.00", CultureInfo.InvariantCulture) } } } }, paymentKey(payment), ct);
        if (Json(create, "status") == "PAYER_ACTION_REQUIRED") return Results.Problem("PayPal requires browser approval for this card; this headless API cannot complete that payment.", statusCode: 422);
        var paypalOrder = Json(create, "id"); var auth = await paypal.SendAsync(HttpMethod.Post, $"v2/checkout/orders/{paypalOrder}/authorize", null, paymentKey(payment), ct); if (Json(auth, "status") == "PAYER_ACTION_REQUIRED") return Results.Problem("PayPal requires browser approval for this card; this headless API cannot complete that payment.", statusCode: 422);
        var authorization = auth.RootElement.GetProperty("purchase_units")[0].GetProperty("payments").GetProperty("authorizations")[0]; payment.Authorized(paypalOrder, authorization.GetProperty("id").GetString()!, authorization.GetProperty("status").GetString()!); await payments.UpdateAsync(payment); return Results.Ok(PaymentDto(payment));
    }
    static async Task<IResult> Fulfil(int orderId, IRepository<Payment> payments, PayPalClient paypal, CancellationToken ct)
    { var p = await payments.FirstOrDefaultAsync(new PaymentByOrderSpec(orderId)); if (p is null) return Results.NotFound(); if (p.Status == PaymentStatus.Captured) return Results.Ok(PaymentDto(p)); if (p.AuthorizationId is null) return Results.BadRequest("Order has no active PayPal authorization."); JsonDocument capture; try { capture = await paypal.SendAsync(HttpMethod.Post, $"v2/payments/authorizations/{p.AuthorizationId}/capture", new { invoice_id = $"ESHOP-{orderId}", final_capture = true }, paymentKey(p), ct); } catch (PayPalException ex) when (ex.Payload.Contains("EXPIRED", StringComparison.OrdinalIgnoreCase) || ex.Payload.Contains("expired", StringComparison.OrdinalIgnoreCase)) { try { var renewed = await paypal.SendAsync(HttpMethod.Post, $"v2/payments/authorizations/{p.AuthorizationId}/reauthorize", new { }, $"{paymentKey(p)}-reauthorize", ct); p.Authorized(p.PayPalOrderId ?? "", Json(renewed, "id"), Json(renewed, "status")); await payments.UpdateAsync(p); capture = await paypal.SendAsync(HttpMethod.Post, $"v2/payments/authorizations/{p.AuthorizationId}/capture", new { invoice_id = $"ESHOP-{orderId}", final_capture = true }, $"{paymentKey(p)}-capture-renewed", ct); } catch (PayPalException renewalFailure) { return Results.Conflict(new { message = "PayPal authorization expired and could not be renewed. Re-authorize the order with a new payment method before fulfilment.", detail = renewalFailure.Message }); } }
        var cap = capture.RootElement; var breakdown = cap.TryGetProperty("seller_receivable_breakdown", out var b) ? b : default; decimal? Money(string n) => breakdown.ValueKind == JsonValueKind.Object && breakdown.TryGetProperty(n, out var x) && decimal.TryParse(x.GetProperty("value").GetString(), NumberStyles.Number, CultureInfo.InvariantCulture, out var v) ? v : null; p.Captured(cap.GetProperty("id").GetString()!, cap.GetProperty("status").GetString()!, decimal.Parse(cap.GetProperty("amount").GetProperty("value").GetString()!, CultureInfo.InvariantCulture), Money("paypal_fee"), Money("net_amount")); await payments.UpdateAsync(p); return Results.Ok(PaymentDto(p)); }
    static async Task<IResult> Cancel(int orderId, IRepository<Payment> payments, PayPalClient paypal, CancellationToken ct)
    { var p = await payments.FirstOrDefaultAsync(new PaymentByOrderSpec(orderId)); if (p is null) return Results.NotFound(); if (p.Status == PaymentStatus.Voided) return Results.Ok(PaymentDto(p)); if (p.Status == PaymentStatus.Captured) return Results.BadRequest("Captured orders cannot be cancelled; use a refund."); if (p.AuthorizationId is not null) { var v = await paypal.SendAsync(HttpMethod.Post, $"v2/payments/authorizations/{p.AuthorizationId}/void", new { }, paymentKey(p), ct); p.Cancelled(Json(v, "status")); } else p.Cancelled("CANCELLED"); await payments.UpdateAsync(p); return Results.Ok(PaymentDto(p)); }
    static async Task<IResult> Refund(int orderId, RefundRequest r, IRepository<Payment> payments, CatalogContext db, PayPalClient paypal, HttpContext http, CancellationToken ct)
    { var p = await payments.FirstOrDefaultAsync(new PaymentByOrderSpec(orderId)); if (p is null || p.BuyerId != User(http)) return Results.NotFound(); if (p.CaptureId is null || p.CapturedAmount is null) return Results.BadRequest("Only captured payments can be refunded."); var amount = r.Amount ?? (p.CapturedAmount.Value - await db.PaymentRefunds.Where(x => x.PaymentId == p.Id && x.Status == "COMPLETED").SumAsync(x => (decimal?)x.Amount, ct) ?? 0m); if (amount <= 0 || amount > p.CapturedAmount.Value - (await db.PaymentRefunds.Where(x => x.PaymentId == p.Id && x.Status == "COMPLETED").SumAsync(x => (decimal?)x.Amount, ct) ?? 0m)) return Results.BadRequest("Refund exceeds the remaining captured amount."); var existing = await db.PaymentRefunds.SingleOrDefaultAsync(x => x.PaymentId == p.Id && x.IdempotencyKey == r.IdempotencyKey, ct); if (existing?.RefundId is not null) return Results.Ok(new { refundId = existing.RefundId, status = existing.Status }); var refund = existing ?? new PaymentRefund(p.Id, r.IdempotencyKey, amount); if (existing is null) db.PaymentRefunds.Add(refund); var result = await paypal.SendAsync(HttpMethod.Post, $"v2/payments/captures/{p.CaptureId}/refund", new { amount = new { currency_code = p.Currency, value = amount.ToString("0.00", CultureInfo.InvariantCulture) }, invoice_id = $"ESHOP-{orderId}" }, r.IdempotencyKey, ct); refund.Completed(Json(result, "id"), Json(result, "status")); await db.SaveChangesAsync(ct); p.Refunded((await db.PaymentRefunds.Where(x => x.PaymentId == p.Id && x.Status == "COMPLETED").SumAsync(x => (decimal?)x.Amount, ct) ?? 0m), refund.Status); await payments.UpdateAsync(p); return Results.Ok(new { refundId = refund.RefundId, status = refund.Status }); }
    static async Task<IResult> MyOrders(IReadRepository<Order> orders, IReadRepository<Payment> payments, HttpContext http) { var os = await orders.ListAsync(new CustomerOrdersWithItemsSpecification(User(http))); var result = new List<object>(); foreach (var o in os) { var p = await payments.FirstOrDefaultAsync(new PaymentByOrderSpec(o.Id)); result.Add(new { orderId = o.Id, orderDate = o.OrderDate, total = o.Total(), payment = p is null ? null : PaymentDto(p) }); } return Results.Ok(result); }
    static async Task<IResult> SaveMethod(PaymentMethodRequest r, IRepository<PaymentMethod> methods, PayPalClient paypal, HttpContext http, CancellationToken ct) { var setup = await paypal.SendAsync(HttpMethod.Post, "v3/vault/setup-tokens", new { payment_source = new { card = new { number = r.Card.Number, expiry = r.Card.Expiry, security_code = r.Card.SecurityCode, name = r.Card.Name, billing_address = new { address_line_1 = r.Card.BillingAddress?.Street, admin_area_2 = r.Card.BillingAddress?.City, admin_area_1 = r.Card.BillingAddress?.State, postal_code = r.Card.BillingAddress?.ZipCode, country_code = r.Card.BillingAddress?.Country } } } }, Guid.NewGuid().ToString("N"), ct); if (Json(setup, "status") != "APPROVED") return Results.Problem("PayPal did not approve the card for vaulting.", statusCode: 422); var token = await paypal.SendAsync(HttpMethod.Post, "v3/vault/payment-tokens", new { payment_source = new { token = new { id = Json(setup, "id"), type = "SETUP_TOKEN" } } }, Guid.NewGuid().ToString("N"), ct); var card = token.RootElement.GetProperty("payment_source").GetProperty("card"); var method = await methods.AddAsync(new PaymentMethod(User(http), Json(token, "id"), card.GetProperty("brand").GetString() ?? "CARD", card.GetProperty("last_digits").GetString() ?? "", card.GetProperty("expiry").GetString() ?? "")); return Results.Created($"/api/payment-methods/{method.Id}", new { paymentMethodId = method.Id, brand = method.Brand, last4 = method.Last4, expiry = method.Expiry }); }
    static async Task<IResult> ListMethods(IReadRepository<PaymentMethod> methods, HttpContext http) => Results.Ok((await methods.ListAsync(new PaymentMethodsSpec(User(http)))).Select(x => new { paymentMethodId = x.Id, brand = x.Brand, last4 = x.Last4, expiry = x.Expiry }));
    static async Task<IResult> DeleteMethod(int id, IRepository<PaymentMethod> methods, PayPalClient paypal, HttpContext http, CancellationToken ct) { var m = await methods.GetByIdAsync(id); if (m is null || m.BuyerId != User(http)) return Results.NotFound(); await paypal.DeleteAsync($"v3/vault/payment-tokens/{m.VaultId}", ct); m.Delete(); await methods.UpdateAsync(m); return Results.NoContent(); }
    static async Task<IResult> Reconciliation(DateTimeOffset from, DateTimeOffset to, PayPalClient paypal, IReadRepository<Payment> payments, IOptions<PayPalOptions> options, CancellationToken ct) { if (to <= from || to - from > TimeSpan.FromDays(366)) return Results.BadRequest("Use a positive range no longer than one year."); var paypalRows = new List<JsonElement>(); for (var cursor = from; cursor < to;) { var end = cursor.AddDays(31) < to ? cursor.AddDays(31) : to; var page = 1; while (true) { var doc = await paypal.GetAsync($"v1/reporting/transactions?start_date={Uri.EscapeDataString(cursor.UtcDateTime.ToString("O"))}&end_date={Uri.EscapeDataString(end.UtcDateTime.ToString("O"))}&transaction_currency={options.Value.Currency}&fields=all&page_size=500&page={page}", ct); if (!doc.RootElement.TryGetProperty("transaction_details", out var rows)) break; paypalRows.AddRange(rows.EnumerateArray()); if (!doc.RootElement.TryGetProperty("total_pages", out var tp) || page >= tp.GetInt32()) break; page++; } cursor = end; } var local = await payments.ListAsync(new AllPaymentsSpec()); var localIds = local.SelectMany(p => new[] { p.AuthorizationId, p.CaptureId }.Where(x => x is not null)).ToHashSet(); return Results.Ok(new { paypal = paypalRows.Select(x => new { transactionId = x.GetProperty("transaction_info").GetProperty("transaction_id").GetString(), status = x.GetProperty("transaction_info").GetProperty("transaction_status").GetString(), amount = x.GetProperty("transaction_info").GetProperty("transaction_amount") }), eshopMissingInPayPal = local.Where(x => x.CaptureId is not null && !paypalRows.Any(y => Json(y, "transaction_info.transaction_id") == x.CaptureId)).Select(x => new { orderId = x.OrderId, captureId = x.CaptureId }), paypalMissingInEshop = paypalRows.Where(x => !localIds.Contains(Json(x, "transaction_info.transaction_id"))).Select(x => new { transactionId = Json(x, "transaction_info.transaction_id"), status = Json(x, "transaction_info.transaction_status") }) }); }
    static object PaymentDto(Payment p) => new { paymentState = p.Status, amount = p.Amount, currency = p.Currency, paypalOrderId = p.PayPalOrderId, authorizationId = p.AuthorizationId, authorizationStatus = p.AuthorizationStatus, captureId = p.CaptureId, captureStatus = p.CaptureStatus, capturedAmount = p.CapturedAmount, paypalFee = p.PayPalFee, netAmount = p.NetAmount };
    static string paymentKey(Payment p) => $"eshop-payment-{p.Id}";
    static string Json(JsonDocument d, string path) => Json(d.RootElement, path);
    static string Json(JsonElement e, string path) { foreach (var part in path.Split('.')) { if (!e.TryGetProperty(part, out e)) return ""; } return e.ValueKind == JsonValueKind.String ? e.GetString() ?? "" : e.ToString(); }
}

public record OrderRequest(List<OrderLine> Items, ShippingAddress? Shipping); public record OrderLine(int CatalogItemId, int Quantity); public record ShippingAddress(string Street, string City, string State, string Country, string ZipCode);
public record PayRequest(CardDetails? Card, int? PaymentMethodId); public record PaymentMethodRequest(CardDetails Card); public record CardDetails(string Number, string Expiry, string SecurityCode, string Name, ShippingAddress? BillingAddress);
public record RefundRequest(decimal? Amount, string IdempotencyKey);
