using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.PaymentEndpoints;

// Contexts built in each route lambda from route params, body and JWT identity.
public record PlaceOrderContext(PlaceOrderRequestBody Body, string? BuyerId);
public record PayContext(int OrderId, PayOrderRequestBody Body, string? BuyerId);
public record RefundContext(int OrderId, RefundRequestBody Body, string? BuyerId);
public record MyOrdersContext(string? BuyerId);
public record ReconciliationContext(DateTimeOffset From, DateTimeOffset To);

/// <summary>POST /api/orders — place an order from catalog items (shopper).</summary>
public class PlaceOrderEndpoint : IEndpoint<IResult, PlaceOrderContext, IPaymentService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (PlaceOrderRequestBody body, IPaymentService service, ClaimsPrincipal user) =>
                await HandleAsync(new PlaceOrderContext(body, PaymentApiHelpers.GetBuyerId(user)), service))
            .Produces<PlaceOrderResponse>(StatusCodes.Status201Created)
            .WithTags("OrderPaymentEndpoints");
    }

    public Task<IResult> HandleAsync(PlaceOrderContext ctx, IPaymentService service) =>
        PaymentApiHelpers.ExecuteAsync(async () =>
        {
            if (ctx.BuyerId is null) return Results.Unauthorized();
            var body = ctx.Body;
            if (body?.Items is null || body.Items.Count == 0)
                return Results.BadRequest(new { error = "An order must contain at least one item." });

            var a = body.ShipToAddress;
            var address = new Address(
                a?.Street ?? "N/A", a?.City ?? "N/A", a?.State ?? "N/A",
                a?.Country ?? "N/A", a?.ZipCode ?? "00000");

            var lines = body.Items.Select(i => new OrderLineRequest(i.CatalogItemId, i.Quantity)).ToList();
            var orderId = await service.PlaceOrderAsync(ctx.BuyerId, lines, address);

            var orders = await service.GetOrdersForBuyerAsync(ctx.BuyerId);
            var placed = orders.FirstOrDefault(o => o.Order.Id == orderId);
            var response = new PlaceOrderResponse
            {
                OrderId = orderId,
                Total = placed?.Order.Total() ?? 0m,
                Currency = placed?.Payment?.CurrencyCode ?? string.Empty,
                Status = placed?.Payment?.Status.ToString() ?? "PendingPayment"
            };
            return Results.Created($"api/orders/{orderId}", response);
        });
}

/// <summary>POST /api/orders/{orderId}/pay — authorize (hold) the order total (shopper).</summary>
public class PayOrderEndpoint : IEndpoint<IResult, PayContext, IPaymentService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders/{orderId}/pay",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int orderId, PayOrderRequestBody body, IPaymentService service, ClaimsPrincipal user) =>
                await HandleAsync(new PayContext(orderId, body, PaymentApiHelpers.GetBuyerId(user)), service))
            .Produces<PaymentView>()
            .WithTags("OrderPaymentEndpoints");
    }

    public Task<IResult> HandleAsync(PayContext ctx, IPaymentService service) =>
        PaymentApiHelpers.ExecuteAsync(async () =>
        {
            if (ctx.BuyerId is null) return Results.Unauthorized();
            var card = ctx.Body?.Card?.ToCardDetails();
            var payment = await service.PayAsync(ctx.OrderId, ctx.BuyerId, card, ctx.Body?.SavedPaymentMethodId);
            return Results.Ok(PaymentView.From(payment));
        });
}

/// <summary>POST /api/orders/{orderId}/fulfil — capture at fulfilment (operator).</summary>
public class FulfilOrderEndpoint : IEndpoint<IResult, int, IPaymentService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders/{orderId}/fulfil",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS,
                AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int orderId, IPaymentService service) => await HandleAsync(orderId, service))
            .Produces<PaymentView>()
            .WithTags("OrderPaymentEndpoints");
    }

    public Task<IResult> HandleAsync(int orderId, IPaymentService service) =>
        PaymentApiHelpers.ExecuteAsync(async () =>
        {
            var payment = await service.FulfilAsync(orderId);
            return Results.Ok(PaymentView.From(payment));
        });
}

/// <summary>POST /api/orders/{orderId}/cancel — release the hold before fulfilment (operator).</summary>
public class CancelOrderEndpoint : IEndpoint<IResult, int, IPaymentService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders/{orderId}/cancel",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS,
                AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int orderId, IPaymentService service) => await HandleAsync(orderId, service))
            .Produces<PaymentView>()
            .WithTags("OrderPaymentEndpoints");
    }

    public Task<IResult> HandleAsync(int orderId, IPaymentService service) =>
        PaymentApiHelpers.ExecuteAsync(async () =>
        {
            var payment = await service.CancelAsync(orderId);
            return Results.Ok(PaymentView.From(payment));
        });
}

/// <summary>POST /api/orders/{orderId}/refunds — refund a captured order, full or partial (shopper).</summary>
public class RefundOrderEndpoint : IEndpoint<IResult, RefundContext, IPaymentService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders/{orderId}/refunds",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int orderId, RefundRequestBody body, IPaymentService service, ClaimsPrincipal user) =>
                await HandleAsync(new RefundContext(orderId, body, PaymentApiHelpers.GetBuyerId(user)), service))
            .Produces<RefundResponse>(StatusCodes.Status201Created)
            .WithTags("OrderPaymentEndpoints");
    }

    public Task<IResult> HandleAsync(RefundContext ctx, IPaymentService service) =>
        PaymentApiHelpers.ExecuteAsync(async () =>
        {
            if (ctx.BuyerId is null) return Results.Unauthorized();
            if (ctx.Body is null || string.IsNullOrWhiteSpace(ctx.Body.IdempotencyKey))
                return Results.BadRequest(new { error = "An idempotency key is required for refunds." });

            var refund = await service.RefundAsync(ctx.OrderId, ctx.BuyerId, ctx.Body.Amount, ctx.Body.IdempotencyKey);

            // Reload payment to report the post-refund totals.
            var orders = await service.GetOrdersForBuyerAsync(ctx.BuyerId);
            var payment = orders.FirstOrDefault(o => o.Order.Id == ctx.OrderId)?.Payment;

            var response = new RefundResponse
            {
                RefundId = refund.PayPalRefundId,
                OrderId = ctx.OrderId,
                Amount = refund.Amount,
                Status = refund.Status,
                TotalRefunded = payment?.TotalRefunded() ?? refund.Amount,
                RefundableRemaining = payment?.RefundableRemaining() ?? 0m
            };
            return Results.Created($"api/orders/{ctx.OrderId}/refunds/{refund.PayPalRefundId}", response);
        });
}

/// <summary>GET /api/my-orders — the caller's orders with payment state (shopper).</summary>
public class MyOrdersEndpoint : IEndpoint<IResult, MyOrdersContext, IPaymentService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/my-orders",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (IPaymentService service, ClaimsPrincipal user) =>
                await HandleAsync(new MyOrdersContext(PaymentApiHelpers.GetBuyerId(user)), service))
            .Produces<List<MyOrderView>>()
            .WithTags("OrderPaymentEndpoints");
    }

    public Task<IResult> HandleAsync(MyOrdersContext ctx, IPaymentService service) =>
        PaymentApiHelpers.ExecuteAsync(async () =>
        {
            if (ctx.BuyerId is null) return Results.Unauthorized();
            var orders = await service.GetOrdersForBuyerAsync(ctx.BuyerId);
            var views = orders.Select(o => new MyOrderView
            {
                OrderId = o.Order.Id,
                OrderDate = o.Order.OrderDate,
                Total = o.Order.Total(),
                Payment = o.Payment is null ? null : PaymentView.From(o.Payment)
            }).ToList();
            return Results.Ok(views);
        });
}

/// <summary>GET /api/reconciliation — PayPal transactions vs eShop orders for a date range (operator).</summary>
public class ReconciliationEndpoint : IEndpoint<IResult, ReconciliationContext, IPaymentService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/reconciliation",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS,
                AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (DateTimeOffset from, DateTimeOffset to, IPaymentService service) =>
                await HandleAsync(new ReconciliationContext(from, to), service))
            .WithTags("OrderPaymentEndpoints");
    }

    public Task<IResult> HandleAsync(ReconciliationContext ctx, IPaymentService service) =>
        PaymentApiHelpers.ExecuteAsync(async () =>
        {
            if (ctx.To < ctx.From)
                return Results.BadRequest(new { error = "'to' must be on or after 'from'." });
            var report = await service.ReconcileAsync(ctx.From, ctx.To);
            return Results.Ok(report);
        });
}
