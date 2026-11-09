using System.Security.Claims;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.PublicApi.Payments;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.PaymentEndpoints;

/// <summary>
/// Places an order for catalog items
/// </summary>
public class CreateOrderEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (CreateOrderRequest request, ClaimsPrincipal user, ShopperService shoppers, OrderPaymentService orders) =>
            {
                var email = await shoppers.ResolveEmailAsync(user);
                var order = await orders.CreateOrderAsync(email, request);
                return Results.Created($"api/orders/{order.OrderId}", order);
            })
            .Produces<OrderDto>(StatusCodes.Status201Created)
            .WithTags("OrderEndpoints");
    }
}

/// <summary>
/// Shows the exact amount (fee and total) the shopper would pay with a payment method
/// </summary>
public class CheckoutEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders/{orderId}/checkout",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int orderId, OrderPaymentMethodRequest request, ClaimsPrincipal user, ShopperService shoppers, OrderPaymentService orders) =>
            {
                var email = await shoppers.ResolveEmailAsync(user);
                return Results.Ok(await orders.CheckoutAsync(email, orderId, request.PaymentMethodId));
            })
            .Produces<CheckoutResponse>()
            .WithTags("OrderEndpoints");
    }
}

/// <summary>
/// Pays an order with a saved card or bank account
/// </summary>
public class PayOrderEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders/{orderId}/pay",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int orderId, OrderPaymentMethodRequest request, ClaimsPrincipal user, ShopperService shoppers, OrderPaymentService orders) =>
            {
                var email = await shoppers.ResolveEmailAsync(user);
                return Results.Ok(await orders.PayAsync(email, orderId, request.PaymentMethodId));
            })
            .Produces<OrderDto>()
            .WithTags("OrderEndpoints");
    }
}

/// <summary>
/// Shows the shopper's order, including whether it has been paid
/// </summary>
public class GetOrderEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/orders/{orderId}",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int orderId, ClaimsPrincipal user, ShopperService shoppers, OrderPaymentService orders) =>
            {
                var email = await shoppers.ResolveEmailAsync(user);
                return Results.Ok(await orders.GetOrderAsync(email, orderId));
            })
            .Produces<OrderDto>()
            .WithTags("OrderEndpoints");
    }
}

/// <summary>
/// Staff: refunds the whole amount of an order
/// </summary>
public class RefundOrderEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/admin/orders/{orderId}/refunds",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS, AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int orderId, OrderPaymentService orders) =>
            {
                var refund = await orders.RefundAsync(orderId);
                return Results.Created($"api/admin/orders/{orderId}", refund);
            })
            .Produces<RefundResponse>(StatusCodes.Status201Created)
            .WithTags("OrderEndpoints");
    }
}

/// <summary>
/// Staff: shows an order with its payment and refund state
/// </summary>
public class GetAdminOrderEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/admin/orders/{orderId}",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS, AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int orderId, OrderPaymentService orders) => Results.Ok(await orders.GetAdminOrderAsync(orderId)))
            .Produces<AdminOrderDto>()
            .WithTags("OrderEndpoints");
    }
}
