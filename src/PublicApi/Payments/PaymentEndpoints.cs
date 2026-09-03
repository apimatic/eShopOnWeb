using System.Globalization;
using System.Collections.Generic;
using System.Linq;
using System;
using System.Threading;
using System.Security.Claims;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.eShopWeb.ApplicationCore.Entities;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.Infrastructure.Data;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.Payments;

public record CreateOrderRequest(IReadOnlyList<OrderLine> Items, string? Street, string? City, string? State, string? Country, string? ZipCode);
public record OrderLine(int CatalogItemId, int Quantity);
public record PayRequest(string? Number, string? Expiry, string? SecurityCode, string? Name, int? PaymentMethodId);
public record SaveCardRequest(string? Number, string? Expiry, string? SecurityCode, string? Name);
public record RefundRequest(decimal? Amount, string IdempotencyKey);
public record OrderIdResponse(int OrderId);

public static class PaymentEndpoints
{
    public static void Map(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders", async (CreateOrderRequest req, ClaimsPrincipal user, CatalogContext db, PayPalSettings settings) =>
        {
            var buyer = UserId(user); if (buyer is null) return Results.Unauthorized();
            if (req.Items is null || req.Items.Count == 0) return Results.BadRequest("At least one catalog item is required.");
            var ids=req.Items.Select(x=>x.CatalogItemId).ToArray();
            var products=await db.CatalogItems.Where(x=>ids.Contains(x.Id)).ToDictionaryAsync(x=>x.Id);
            if (products.Count != ids.Distinct().Count() || req.Items.Any(x=>x.Quantity<=0 || !products.ContainsKey(x.CatalogItemId))) return Results.BadRequest("Invalid catalog item or quantity.");
            var address=new Address(req.Street??"",req.City??"",req.State??"",req.Country??"",req.ZipCode??"");
            var items=req.Items.Select(x=>new OrderItem(new CatalogItemOrdered(x.CatalogItemId,products[x.CatalogItemId].Name,products[x.CatalogItemId].PictureUri),products[x.CatalogItemId].Price,x.Quantity)).ToList();
            var order=new Order(buyer,address,items); db.Orders.Add(order); await db.SaveChangesAsync();
            order.AttachPayment(new PaymentRecord(order.Id, settings.Currency)); db.PaymentRecords.Add(order.Payment!); await db.SaveChangesAsync();
            return Results.Created($"api/orders/{order.Id}", new { orderId=order.Id, total=order.Total(), paymentState=order.PaymentState.ToString() });
        }).RequireAuthorization().WithTags("PaymentEndpoints");

        app.MapPost("api/orders/{orderId:int}/pay", async (int orderId, PayRequest req, ClaimsPrincipal user, CatalogContext db, PayPalGateway paypal, CancellationToken ct) =>
        {
            var buyer=UserId(user); var order=await db.Orders.Include(x=>x.Payment).ThenInclude(x=>x!.Refunds).Include(x=>x.OrderItems).SingleOrDefaultAsync(x=>x.Id==orderId && x.BuyerId==buyer);
            if (order is null) return Results.NotFound(); if (order.PaymentState is OrderPaymentState.Authorized or OrderPaymentState.Captured) return Results.Ok(PaymentDto(order));
            if (req.PaymentMethodId is not null && !await db.PaymentMethods.AnyAsync(x=>x.Id==req.PaymentMethodId && x.OwnerId==buyer)) return Results.NotFound();
            if (req.PaymentMethodId is null && string.IsNullOrWhiteSpace(req.Number)) return Results.BadRequest("Card details or paymentMethodId is required.");
            try {
                var pp=await paypal.CreateOrder(order.Total(),ct); var vault=req.PaymentMethodId is null?null:(await db.PaymentMethods.FindAsync(req.PaymentMethodId))!.PayPalTokenId;
                var auth=await paypal.Authorize(pp.Id!,req.Number,req.Expiry,req.SecurityCode,req.Name,vault,ct);
                var a=auth.PurchaseUnits?.FirstOrDefault()?.Payments?.Authorizations?.FirstOrDefault();
                if (a?.Id is null) return Results.BadRequest("PayPal did not return an authorization.");
                order.Payment!.SetAuthorization(pp.Id!,a.Id,a.Status?.ToString()??"UNKNOWN"); order.SetPaymentState(OrderPaymentState.Authorized); await db.SaveChangesAsync();
                return Results.Ok(PaymentDto(order));
            } catch(Exception) { order.Payment!.Fail("PayPal payment failed."); await db.SaveChangesAsync(); return Results.BadRequest(new { error="Payment was not authorized." }); }
        }).RequireAuthorization().WithTags("PaymentEndpoints");

        app.MapPost("api/orders/{orderId:int}/fulfil", async (int orderId, ClaimsPrincipal user, CatalogContext db, PayPalGateway paypal, CancellationToken ct) =>
        {
            var order=await db.Orders.Include(x=>x.Payment).SingleOrDefaultAsync(x=>x.Id==orderId); if (order is null) return Results.NotFound();
            try {
                var p=order.Payment!; var auth=await paypal.GetAuthorization(p.AuthorizationId!,ct);
                if (auth.Status?.ToString()=="PENDING" || auth.Status?.ToString()=="CREATED" && DateTimeOffset.TryParse(auth.ExpirationTime, out var expiry) && expiry < DateTimeOffset.UtcNow) auth=await paypal.Reauthorize(p.AuthorizationId!,order.Total(),ct);
                var capture=await paypal.Capture(p.AuthorizationId!,order.Total(),ct); var c=capture.Amount;
                decimal amount=decimal.Parse(c?.Value??"0",CultureInfo.InvariantCulture), fee=decimal.Parse(capture.SellerReceivableBreakdown?.PaypalFee?.Value??"0",CultureInfo.InvariantCulture), net=decimal.Parse(capture.SellerReceivableBreakdown?.NetAmount?.Value??"0",CultureInfo.InvariantCulture);
                p.SetCapture(capture.Id!,capture.Status?.ToString()??"UNKNOWN",amount,fee,net); order.SetPaymentState(OrderPaymentState.Captured); await db.SaveChangesAsync(); return Results.Ok(PaymentDto(order));
            } catch(Exception) { return Results.Conflict(new { error="Fulfilment could not capture the authorization; operator must re-authorize or collect payment again." }); }
        }).RequireAuthorization(BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS).WithTags("PaymentEndpoints");

        app.MapPost("api/orders/{orderId:int}/cancel", async (int orderId, CatalogContext db, PayPalGateway paypal, CancellationToken ct) =>
        { var o=await db.Orders.Include(x=>x.Payment).SingleOrDefaultAsync(x=>x.Id==orderId); if(o is null)return Results.NotFound(); if(o.PaymentState!=OrderPaymentState.Authorized)return Results.Conflict("Only authorized orders can be cancelled."); await paypal.Void(o.Payment!.AuthorizationId!,ct); o.Payment.Cancel();o.SetPaymentState(OrderPaymentState.Cancelled);await db.SaveChangesAsync();return Results.Ok(PaymentDto(o)); }).RequireAuthorization(BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS).WithTags("PaymentEndpoints");

        app.MapPost("api/orders/{orderId:int}/refunds", async (int orderId, RefundRequest req, CatalogContext db, PayPalGateway paypal, CancellationToken ct) =>
        { var o=await db.Orders.Include(x=>x.Payment).ThenInclude(x=>x!.Refunds).SingleOrDefaultAsync(x=>x.Id==orderId);if(o is null)return Results.NotFound();if(o.PaymentState!=OrderPaymentState.Captured)return Results.Conflict("Order has not been captured.");if(string.IsNullOrWhiteSpace(req.IdempotencyKey))return Results.BadRequest("IdempotencyKey is required.");var prior=o.Payment!.Refunds.SingleOrDefault(x=>x.IdempotencyKey==req.IdempotencyKey);if(prior is not null)return Results.Ok(new{refundId=prior.Id,amount=prior.Amount,status=prior.Status});var used=o.Payment.Refunds.Sum(x=>x.Amount);var amount=req.Amount??(o.Payment.CapturedAmount-used);if(amount<=0||amount>o.Payment.CapturedAmount-used)return Results.BadRequest("Refund exceeds the captured amount.");var r=await paypal.Refund(o.Payment.CaptureId!,req.Amount,req.IdempotencyKey,ct);var rr=new PaymentRefund(req.IdempotencyKey,r.Id!,amount,used+amount,r.Status?.ToString()??"UNKNOWN");o.Payment.AddRefund(rr);db.PaymentRefunds.Add(rr);await db.SaveChangesAsync();return Results.Ok(new{refundId=rr.Id,amount=rr.Amount,status=rr.Status}); }).RequireAuthorization().WithTags("PaymentEndpoints");

        app.MapGet("api/my-orders", async (ClaimsPrincipal user,CatalogContext db) => Results.Ok(await db.Orders.Include(x=>x.Payment).ThenInclude(x=>x!.Refunds).Where(x=>x.BuyerId==UserId(user)).Select(x=>new{orderId=x.Id,total=x.Total(),orderDate=x.OrderDate,paymentState=x.PaymentState.ToString(),payment=x.Payment}).ToListAsync())).RequireAuthorization().WithTags("PaymentEndpoints");
        app.MapPost("api/payment-methods", async (SaveCardRequest req,ClaimsPrincipal user,CatalogContext db,PayPalGateway paypal, CancellationToken ct) => {var id=UserId(user)!;var r=await paypal.SaveCard(id,req.Number,req.Expiry,req.SecurityCode,req.Name,ct);var card=r.PaymentSource?.Card;var m=new ApplicationCore.Entities.BuyerAggregate.PaymentMethod(id,r.Id!,card?.LastDigits,card?.Brand?.ToString());db.PaymentMethods.Add(m);await db.SaveChangesAsync();return Results.Ok(new{paymentMethodId=m.Id,last4=m.Last4,brand=m.Brand});}).RequireAuthorization().WithTags("PaymentEndpoints");
        app.MapGet("api/payment-methods", async (ClaimsPrincipal user,CatalogContext db) => Results.Ok(await db.PaymentMethods.Where(x=>x.OwnerId==UserId(user)).Select(x=>new{paymentMethodId=x.Id,last4=x.Last4,brand=x.Brand}).ToListAsync())).RequireAuthorization().WithTags("PaymentEndpoints");
        app.MapDelete("api/payment-methods/{id:int}", async (int id,ClaimsPrincipal user,CatalogContext db,PayPalGateway paypal, CancellationToken ct) => {var m=await db.PaymentMethods.SingleOrDefaultAsync(x=>x.Id==id&&x.OwnerId==UserId(user));if(m is null)return Results.NotFound();await paypal.DeleteCard(m.PayPalTokenId,ct);db.PaymentMethods.Remove(m);await db.SaveChangesAsync();return Results.NoContent();}).RequireAuthorization().WithTags("PaymentEndpoints");
        app.MapGet("api/reconciliation", async (DateTimeOffset from,DateTimeOffset to,CatalogContext db,PayPalGateway paypal, CancellationToken ct) => {var all=new List<object>();for(var page=1;;page++){var r=await paypal.Search(from.UtcDateTime.ToString("O"),to.UtcDateTime.ToString("O"),page,ct);if(r.TransactionDetails is not null)all.AddRange(r.TransactionDetails.Cast<object>());if(r.TotalPages is null||page>=r.TotalPages)break;}var orders=await db.PaymentRecords.ToListAsync();return Results.Ok(new{from,to,paypalTransactions=all,eshopPayments=orders});}).RequireAuthorization(BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS).WithTags("PaymentEndpoints");
    }
    private static string? UserId(ClaimsPrincipal u)=>u.FindFirstValue(ClaimTypes.NameIdentifier)??u.Identity?.Name;
    private static object PaymentDto(Order o)=>new{orderId=o.Id,total=o.Total(),paymentState=o.PaymentState.ToString(),authorizationId=o.Payment?.AuthorizationId,captureId=o.Payment?.CaptureId,capturedAmount=o.Payment?.CapturedAmount,paypalFee=o.Payment?.PayPalFee,netProceeds=o.Payment?.NetProceeds};
}
