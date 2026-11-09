using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.Payments;
using Microsoft.eShopWeb.Infrastructure.Data;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.Payments;

public record OrderItemRequest(int CatalogItemId, int Quantity);
public record ShipToRequest(string? Street, string? City, string? State, string? Country, string? ZipCode);
public record PlaceOrderRequest(List<OrderItemRequest>? Items, ShipToRequest? ShipTo);
public record PaymentMethodRequest(string? PaymentMethodId);

public static class OrderViews
{
    public static object Shopper(Order order, OrderPayment? p) => new
    {
        id = order.Id,
        orderDate = order.OrderDate,
        items = order.OrderItems.Select(i => new
        {
            catalogItemId = i.ItemOrdered.CatalogItemId,
            name = i.ItemOrdered.ProductName,
            unitPrice = i.UnitPrice,
            units = i.Units
        }),
        subtotal = order.Total(),
        fee = p?.Fee ?? 0m,
        total = p != null && p.Total > 0 ? p.Total : order.Total(),
        paid = p?.PaymentStatus == PaymentStatuses.Paid,
        paymentStatus = p?.PaymentStatus ?? PaymentStatuses.None,
        paymentMethodId = p?.PaymentMethodId,
        paymentMethodType = p?.Method,
        refundStatus = p?.RefundStatus ?? RefundStatuses.None
    };

    public static object Admin(Order order, OrderPayment? p) => new
    {
        id = order.Id,
        buyer = order.BuyerId,
        orderDate = order.OrderDate,
        items = order.OrderItems.Select(i => new
        {
            catalogItemId = i.ItemOrdered.CatalogItemId,
            name = i.ItemOrdered.ProductName,
            unitPrice = i.UnitPrice,
            units = i.Units
        }),
        subtotal = order.Total(),
        fee = p?.Fee ?? 0m,
        total = p != null && p.Total > 0 ? p.Total : order.Total(),
        paid = p?.PaymentStatus == PaymentStatuses.Paid,
        paymentStatus = p?.PaymentStatus ?? PaymentStatuses.None,
        paymentFailure = p?.FailureReason,
        paymentMethodId = p?.PaymentMethodId,
        paymentMethodType = p?.Method,
        staxTransactionId = p?.StaxTransactionId,
        refundStatus = p?.RefundStatus ?? RefundStatuses.None,
        refundNote = p?.RefundNote,
        refundRequestedAt = p?.RefundRequestedAt,
        refundedAt = p?.RefundedAt
    };
}

/// <summary>Places an order for catalog items.</summary>
public class PlaceOrderEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (PlaceOrderRequest request, ClaimsPrincipal user, ShopperAccessor shopper, CatalogContext db, CancellationToken ct) =>
            {
                var email = await shopper.EmailAsync(user);
                if (request.Items == null || request.Items.Count == 0) throw new ApiException(400, "items must not be empty.");
                if (request.Items.Any(i => i.Quantity < 1 || i.Quantity > 1000)) throw new ApiException(400, "Each quantity must be between 1 and 1000.");

                var wanted = request.Items.GroupBy(i => i.CatalogItemId).ToDictionary(g => g.Key, g => g.Sum(i => i.Quantity));
                var ids = wanted.Keys.ToList();
                var catalog = await db.CatalogItems.Where(c => ids.Contains(c.Id)).ToListAsync(ct);
                var missing = ids.Where(id => catalog.All(c => c.Id != id)).ToList();
                if (missing.Count > 0) throw new ApiException(404, $"Catalog item(s) not found: {string.Join(", ", missing)}.");

                var lines = catalog.Select(c => new OrderItem(
                    new CatalogItemOrdered(c.Id, c.Name, string.IsNullOrEmpty(c.PictureUri) ? "none" : c.PictureUri),
                    c.Price, wanted[c.Id])).ToList();

                var s = request.ShipTo;
                var address = new Address(s?.Street ?? "N/A", s?.City ?? "N/A", s?.State ?? "", s?.Country ?? "N/A", s?.ZipCode ?? "N/A");
                var order = new Order(email, address, lines);
                db.Orders.Add(order);
                await db.SaveChangesAsync(ct);
                return Results.Created($"api/orders/{order.Id}", OrderViews.Shopper(order, null));
            })
            .WithTags("OrderEndpoints");
    }
}

/// <summary>Shows the exact amount, fee included, for a payment method.</summary>
public class CheckoutEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders/{orderId:int}/checkout",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int orderId, PaymentMethodRequest request, ClaimsPrincipal user, ShopperAccessor shopper, OrderPaymentService orders, CancellationToken ct) =>
            {
                var email = await shopper.EmailAsync(user);
                var order = await orders.GetOrderAsync(orderId, email, ct);
                var existing = await orders.GetOrCreatePaymentAsync(order, ct);
                if (existing.PaymentStatus is PaymentStatuses.Paid or PaymentStatuses.Pending)
                    throw new ApiException(409, "This order is already paid.");
                var quote = await orders.QuoteAsync(order, email, request.PaymentMethodId ?? "", ct);
                return Results.Ok(new
                {
                    orderId,
                    paymentMethodId = quote.PaymentMethodId,
                    paymentMethodType = quote.Method,
                    subtotal = quote.Amount,
                    fee = quote.Fee,
                    total = quote.Total
                });
            })
            .WithTags("OrderEndpoints");
    }
}

/// <summary>Pays an order with a saved card or bank account.</summary>
public class PayOrderEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders/{orderId:int}/pay",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int orderId, PaymentMethodRequest request, ClaimsPrincipal user, ShopperAccessor shopper, OrderPaymentService orders, CancellationToken ct) =>
            {
                var email = await shopper.EmailAsync(user);
                var order = await orders.GetOrderAsync(orderId, email, ct);
                var payment = await orders.PayAsync(order, email, request.PaymentMethodId ?? "", ct);
                return Results.Ok(OrderViews.Shopper(order, payment));
            })
            .WithTags("OrderEndpoints");
    }
}

/// <summary>Shows an order, with whether its payment has gone through.</summary>
public class GetOrderEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/orders/{orderId:int}",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int orderId, ClaimsPrincipal user, ShopperAccessor shopper, OrderPaymentService orders, CancellationToken ct) =>
            {
                var email = await shopper.EmailAsync(user);
                var order = await orders.GetOrderAsync(orderId, email, ct);
                var payment = await orders.GetOrCreatePaymentAsync(order, ct);
                await OrderSync.TryRefreshAsync(orders, payment, ct);
                return Results.Ok(OrderViews.Shopper(order, payment));
            })
            .WithTags("OrderEndpoints");
    }
}

public static class OrderSync
{
    /// <summary>Settles what Stax has decided since last time. A Stax outage must not break reading an order.</summary>
    public static async Task TryRefreshAsync(OrderPaymentService orders, OrderPayment payment, CancellationToken ct)
    {
        try
        {
            await orders.RefreshPaymentAsync(payment, ct);
            await orders.TryRefundAsync(payment, ct);
        }
        catch (ApiException)
        {
        }
    }
}

/// <summary>Staff refund an order in full.</summary>
public class RefundOrderEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/admin/orders/{orderId:int}/refunds",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS, AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int orderId, OrderPaymentService orders, CancellationToken ct) =>
            {
                var order = await orders.GetOrderAsync(orderId, null, ct);
                var payment = await orders.RequestRefundAsync(order, ct);
                return Results.Ok(new
                {
                    orderId,
                    amount = payment.Total,
                    refundStatus = payment.RefundStatus,
                    refundNote = payment.RefundNote
                });
            })
            .WithTags("AdminOrderEndpoints");
    }
}

/// <summary>Staff view of an order, with its refund status.</summary>
public class AdminGetOrderEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/admin/orders/{orderId:int}",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS, AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int orderId, OrderPaymentService orders, CancellationToken ct) =>
            {
                var order = await orders.GetOrderAsync(orderId, null, ct);
                var payment = await orders.GetOrCreatePaymentAsync(order, ct);
                await OrderSync.TryRefreshAsync(orders, payment, ct);
                return Results.Ok(OrderViews.Admin(order, payment));
            })
            .WithTags("AdminOrderEndpoints");
    }
}
