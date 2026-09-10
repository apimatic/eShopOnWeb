using System.Linq;
using System.Security.Claims;
using System.Threading;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.PayPal;
using MinimalApi.Endpoint;
using AdminRole = BlazorShared.Authorization.Constants.Roles;

namespace Microsoft.eShopWeb.PublicApi.PaymentEndpoints;

/// <summary>POST /api/orders — place an order from catalog items (shopper). Returns orderId.</summary>
public class PlaceOrderEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
            async (PlaceOrderRequest request, IPaymentService service, PayPalSettings settings,
                ClaimsPrincipal user, CancellationToken ct) =>
            {
                var buyerId = user.GetBuyerId();
                var items = request.Items
                    .Select(i => new OrderLineRequest(i.CatalogItemId, i.Quantity))
                    .ToList();
                var orderId = await service.PlaceOrderAsync(buyerId, items, request.ShipToAddress?.ToAddress(), ct);
                var view = await service.GetOrderViewAsync(orderId, buyerId, ct);
                return Results.Created($"api/orders/{orderId}", new PlaceOrderResponse
                {
                    OrderId = orderId,
                    Total = view.Order.Total(),
                    Currency = settings.Currency,
                    Status = view.Order.Status.ToString()
                });
            })
            .Produces<PlaceOrderResponse>(StatusCodes.Status201Created)
            .WithTags("Orders");
    }
}

/// <summary>POST /api/orders/{orderId}/pay — authorize (hold) the order total (shopper).</summary>
public class PayOrderEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders/{orderId:int}/pay",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
            async (int orderId, PayOrderRequest request, IPaymentService service,
                ClaimsPrincipal user, CancellationToken ct) =>
            {
                var buyerId = user.GetBuyerId();
                var instruction = new PaymentInstruction
                {
                    Card = request.Card?.ToCardDetails(),
                    SavedPaymentMethodId = request.SavedPaymentMethodId
                };
                await service.AuthorizeOrderAsync(orderId, buyerId, instruction, ct);
                var view = await service.GetOrderViewAsync(orderId, buyerId, ct);
                return Results.Ok(OrderPaymentDto.From(view));
            })
            .Produces<OrderPaymentDto>()
            .WithTags("Orders");
    }
}

/// <summary>POST /api/orders/{orderId}/fulfil — fulfil and capture (operator).</summary>
public class FulfilOrderEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders/{orderId:int}/fulfil",
            [Authorize(Roles = AdminRole.ADMINISTRATORS, AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
            async (int orderId, IPaymentService service, CancellationToken ct) =>
            {
                await service.FulfilOrderAsync(orderId, ct);
                var view = await service.GetOrderViewAsync(orderId, null, ct);
                return Results.Ok(OrderPaymentDto.From(view));
            })
            .Produces<OrderPaymentDto>()
            .WithTags("Orders");
    }
}

/// <summary>POST /api/orders/{orderId}/cancel — cancel before fulfilment, releasing funds (operator).</summary>
public class CancelOrderEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders/{orderId:int}/cancel",
            [Authorize(Roles = AdminRole.ADMINISTRATORS, AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
            async (int orderId, IPaymentService service, CancellationToken ct) =>
            {
                await service.CancelOrderAsync(orderId, ct);
                var view = await service.GetOrderViewAsync(orderId, null, ct);
                return Results.Ok(OrderPaymentDto.From(view));
            })
            .Produces<OrderPaymentDto>()
            .WithTags("Orders");
    }
}

/// <summary>POST /api/orders/{orderId}/refunds — refund a captured order, full or partial (shopper). Returns refundId.</summary>
public class RefundOrderEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders/{orderId:int}/refunds",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
            async (int orderId, RefundRequest request, IPaymentService service,
                ClaimsPrincipal user, HttpContext http, CancellationToken ct) =>
            {
                var buyerId = user.GetBuyerId();
                var key = !string.IsNullOrWhiteSpace(request.IdempotencyKey)
                    ? request.IdempotencyKey
                    : http.Request.Headers["Idempotency-Key"].ToString();
                if (string.IsNullOrWhiteSpace(key))
                {
                    return Results.BadRequest(new { message = "A refund requires an idempotencyKey (body field or Idempotency-Key header)." });
                }
                var refundId = await service.RefundOrderAsync(orderId, buyerId, request.Amount, key, ct);
                var view = await service.GetOrderViewAsync(orderId, buyerId, ct);
                return Results.Ok(new RefundResponse { RefundId = refundId, Order = OrderPaymentDto.From(view) });
            })
            .Produces<RefundResponse>()
            .WithTags("Orders");
    }
}

/// <summary>GET /api/my-orders — the caller's orders with payment state (shopper).</summary>
public class MyOrdersEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/my-orders",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
            async (IPaymentService service, ClaimsPrincipal user, CancellationToken ct) =>
            {
                var buyerId = user.GetBuyerId();
                var views = await service.GetMyOrdersAsync(buyerId, ct);
                return Results.Ok(views.Select(OrderPaymentDto.From).ToList());
            })
            .Produces<System.Collections.Generic.List<OrderPaymentDto>>()
            .WithTags("Orders");
    }
}
