using System.Security.Claims;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.eShopWeb.ApplicationCore.Entities;
using Microsoft.eShopWeb.ApplicationCore.Entities.BuyerAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.Infrastructure.Data;

namespace Microsoft.eShopWeb.PublicApi.Payments;

public static class PaymentEndpoints
{
    private const string Admin = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS;
    private static string User(ClaimsPrincipal p) => p.FindFirstValue(ClaimTypes.Name) ?? throw new UnauthorizedAccessException();
    public static void MapPaymentEndpoints(this IEndpointRouteBuilder app)
    {
        var shopper = new Action<Microsoft.AspNetCore.Builder.RouteHandlerBuilder>(b => b.RequireAuthorization(new AuthorizeAttribute { AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme }));
        app.MapPost("api/orders", async (CreateOrderRequest r, CatalogContext db, ClaimsPrincipal p) =>
        {
            if (r.Items is null || r.Items.Count == 0) return Results.BadRequest("At least one catalog item is required.");
            var ids = r.Items.Select(x => x.CatalogItemId).ToArray();
            var catalog = await db.CatalogItems.Where(x => ids.Contains(x.Id)).ToListAsync();
            if (catalog.Count != ids.Distinct().Count() || r.Items.Any(x => x.Quantity <= 0)) return Results.BadRequest("Catalog item or quantity is invalid.");
            var items = r.Items.Select(x => { var c = catalog.Single(y => y.Id == x.CatalogItemId); return new OrderItem(new CatalogItemOrdered(c.Id, c.Name, c.PictureUri), c.Price, x.Quantity); }).ToList();
            var order = new Order(User(p), new Address(r.Street, r.City, r.State, r.Country, r.ZipCode), items);
            db.Orders.Add(order); await db.SaveChangesAsync();
            return Results.Created($"api/orders/{order.Id}", new { orderId = order.Id, order.PaymentStatus, total = order.Total() });
        }).Apply(shopper);

        app.MapPost("api/orders/{orderId:int}/pay", async (int orderId, PayRequest r, CatalogContext db, IPayPalPaymentService pp, PayPalSettings s, ClaimsPrincipal p, CancellationToken ct) =>
        {
            var o = await db.Orders.Include(x => x.OrderItems).SingleOrDefaultAsync(x => x.Id == orderId && x.BuyerId == User(p), ct);
            if (o is null) return Results.NotFound(); if (o.PaymentStatus == "Authorized") return Results.Ok(new { orderId, o.PaymentStatus, authorizationId = o.PaymentAuthorizationId });
            if (o.PaymentStatus != "AwaitingPayment") return Results.Conflict(new { message = "The order is no longer awaiting payment." });
            if (string.IsNullOrWhiteSpace(r.SavedCardId) && string.IsNullOrWhiteSpace(r.CardNumber)) return Results.BadRequest("Card details or savedCardId is required.");
            var pm = r.SavedCardId is null ? null : await db.PaymentMethods.SingleOrDefaultAsync(x => x.Id.ToString() == r.SavedCardId && x.OwnerId == User(p), ct);
            if (r.SavedCardId is not null && pm is null) return Results.NotFound("Saved card not found.");
            var a = await pp.AuthorizeAsync(o.Total(), s.Currency, r.CardNumber, r.Expiry, r.SecurityCode, r.Name, pm?.CardId, $"order-{o.Id}-authorize", ct);
            o.SetAuthorization(a.Id!); o.SetSavedPaymentMethod(pm?.Id); await db.SaveChangesAsync(ct);
            return Results.Ok(new { orderId, o.PaymentStatus, authorizationId = o.PaymentAuthorizationId });
        }).Apply(shopper);

        app.MapPost("api/orders/{orderId:int}/fulfil", async (int orderId, CatalogContext db, IPayPalPaymentService pp, PayPalSettings s, ClaimsPrincipal p, CancellationToken ct) =>
        {
            var o = await db.Orders.Include(x => x.OrderItems).SingleOrDefaultAsync(x => x.Id == orderId, ct); if (o is null) return Results.NotFound();
            if (o.PaymentStatus == "Captured") return Results.Ok(new { orderId, o.PaymentStatus, o.CapturedAmount, o.PaymentFee, o.NetProceeds });
            if (o.PaymentAuthorizationId is null || o.PaymentStatus != "Authorized") return Results.Conflict(new { message = "The order must have an active authorization before fulfilment." });
            var current = await pp.GetAuthorizationAsync(o.PaymentAuthorizationId, ct);
            if (DateTimeOffset.TryParse(current.ExpirationTime, out var expiry) && expiry <= DateTimeOffset.UtcNow)
            {
                if (!o.SavedPaymentMethodId.HasValue) return Results.Conflict(new { message = "The PayPal authorization has expired. The shopper must pay the order again; fulfilment was not completed." });
                var saved = await db.PaymentMethods.SingleOrDefaultAsync(x => x.Id == o.SavedPaymentMethodId.Value && x.OwnerId == o.BuyerId, ct);
                if (saved is null) return Results.Conflict(new { message = "The saved card used for renewal no longer exists. Ask the shopper to choose another payment method." });
                var renewed = await pp.AuthorizeAsync(o.Total(), s.Currency, null, null, null, null, saved.CardId, $"order-{o.Id}-renew", ct);
                o.SetAuthorization(renewed.Id!); await db.SaveChangesAsync(ct);
            }
            var c = await pp.CaptureAsync(o.PaymentAuthorizationId, $"order-{o.Id}-capture", ct);
            var captured = decimal.Parse(c.Amount?.Value ?? o.Total().ToString("0.00"), System.Globalization.CultureInfo.InvariantCulture);
            var fee = decimal.Parse(c.SellerReceivableBreakdown?.PaypalFee?.Value ?? "0", System.Globalization.CultureInfo.InvariantCulture);
            o.SetFulfilled(c.Id!, captured, fee, captured - fee); await db.SaveChangesAsync(ct);
            return Results.Ok(new { orderId, o.PaymentStatus, capturedAmount = o.CapturedAmount, paymentFee = o.PaymentFee, netProceeds = o.NetProceeds });
        }).RequireAuthorization(new AuthorizeAttribute { Roles = Admin, AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme });

        app.MapPost("api/orders/{orderId:int}/cancel", async (int orderId, CatalogContext db, IPayPalPaymentService pp, ClaimsPrincipal p, CancellationToken ct) =>
        { var o = await db.Orders.SingleOrDefaultAsync(x => x.Id == orderId, ct); if (o is null) return Results.NotFound(); if (o.PaymentStatus == "Cancelled") return Results.Ok(new { orderId, o.PaymentStatus }); if (o.PaymentStatus != "Authorized") return Results.Conflict(); await pp.VoidAsync(o.PaymentAuthorizationId!, $"order-{o.Id}-void", ct); o.Cancel(); await db.SaveChangesAsync(ct); return Results.Ok(new { orderId, o.PaymentStatus }); }).RequireAuthorization(new AuthorizeAttribute { Roles = Admin, AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme });
        app.MapPost("api/orders/{orderId:int}/refunds", async (int orderId, RefundRequest r, CatalogContext db, IPayPalPaymentService pp, ClaimsPrincipal p, CancellationToken ct) =>
        { var o = await db.Orders.SingleOrDefaultAsync(x => x.Id == orderId, ct); if (o is null) return Results.NotFound(); if (o.BuyerId != User(p) && !p.IsInRole(Admin)) return Results.Forbid(); var amount = r.Amount ?? o.CapturedAmount - o.RefundedAmount; if (amount <= 0 || amount > o.CapturedAmount - o.RefundedAmount) return Results.BadRequest("Refund exceeds the captured, not-yet-refunded amount."); if (string.IsNullOrWhiteSpace(r.IdempotencyKey)) return Results.BadRequest("IdempotencyKey is required."); if (o.RefundIdempotencyKeys.Split(',', StringSplitOptions.RemoveEmptyEntries).Contains(r.IdempotencyKey)) return Results.Ok(new { refundId = r.IdempotencyKey, amount, o.PaymentStatus }); var x = await pp.RefundAsync(o.PaymentCaptureId!, amount, r.IdempotencyKey, ct); o.AddRefund(amount, r.IdempotencyKey); await db.SaveChangesAsync(ct); return Results.Ok(new { refundId = x.Id, amount, o.PaymentStatus }); }).Apply(shopper);

        app.MapGet("api/my-orders", async (CatalogContext db, ClaimsPrincipal p) => Results.Ok(await db.Orders.Where(x => x.BuyerId == User(p)).Select(x => new { orderId = x.Id, x.OrderDate, total = x.OrderItems.Sum(i => i.UnitPrice * i.Units), x.PaymentStatus, x.PaymentAuthorizationId, x.PaymentCaptureId, x.CapturedAmount, x.RefundedAmount }).ToListAsync())).Apply(shopper);
        app.MapPost("api/payment-methods", async (SaveCardRequest r, CatalogContext db, IPayPalPaymentService pp, ClaimsPrincipal p, CancellationToken ct) => { var x = await pp.SaveCardAsync(r.CardNumber, r.Expiry, r.SecurityCode, r.Name, $"card-{Guid.NewGuid():N}", ct); var pm = new PaymentMethod(User(p), x.Id!, r.Brand ?? "card", r.CardNumber[^4..], r.Expiry); db.PaymentMethods.Add(pm); await db.SaveChangesAsync(ct); return Results.Created($"api/payment-methods/{pm.Id}", new { paymentMethodId = pm.Id, brand = pm.Alias, last4 = pm.Last4, expiry = pm.Expiry }); }).Apply(shopper);
        app.MapGet("api/payment-methods", async (CatalogContext db, ClaimsPrincipal p) => Results.Ok(await db.PaymentMethods.Where(x => x.OwnerId == User(p)).Select(x => new { paymentMethodId = x.Id, brand = x.Alias, last4 = x.Last4, expiry = x.Expiry }).ToListAsync())).Apply(shopper);
        app.MapDelete("api/payment-methods/{id:int}", async (int id, CatalogContext db, IPayPalPaymentService pp, ClaimsPrincipal p, CancellationToken ct) => { var x = await db.PaymentMethods.SingleOrDefaultAsync(x => x.Id == id && x.OwnerId == User(p), ct); if (x is null) return Results.NotFound(); await pp.DeleteCardAsync(x.CardId!, ct); db.PaymentMethods.Remove(x); await db.SaveChangesAsync(ct); return Results.NoContent(); }).Apply(shopper);
        app.MapGet("api/reconciliation", async (DateTimeOffset from, DateTimeOffset to, IPayPalPaymentService pp, CatalogContext db, CancellationToken ct) => Results.Ok(new { from, to, transactions = await pp.SearchAsync(from, to, ct), orders = await db.Orders.Where(x => x.OrderDate >= from && x.OrderDate <= to).Select(x => new { orderId = x.Id, x.PaymentCaptureId, x.PaymentStatus, x.CapturedAmount }).ToListAsync(ct) })).RequireAuthorization(new AuthorizeAttribute { Roles = Admin, AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme });
    }
    private static RouteHandlerBuilder Apply(this RouteHandlerBuilder b, Action<RouteHandlerBuilder> action) { action(b); return b; }
}

public sealed record CreateOrderRequest(List<CreateOrderLine> Items, string Street, string City, string State, string Country, string ZipCode);
public sealed record CreateOrderLine(int CatalogItemId, int Quantity);
public sealed record PayRequest(string? CardNumber, string? Expiry, string? SecurityCode, string? Name, string? SavedCardId);
public sealed record SaveCardRequest(string CardNumber, string Expiry, string SecurityCode, string? Name, string? Brand);
public sealed record RefundRequest(decimal? Amount, string IdempotencyKey);
