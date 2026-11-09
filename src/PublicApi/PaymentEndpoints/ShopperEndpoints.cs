using System;
using System.Security.Claims;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.PublicApi.Payments;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.PaymentEndpoints;

/// <summary>Shopper features: saved payment methods, orders, checkout, pay, order status, closing the account.</summary>
public class ShopperEndpoints : IEndpoint
{
    private const string Jwt = JwtBearerDefaults.AuthenticationScheme;

    public void AddRoute(IEndpointRouteBuilder app)
    {
        // saved payment methods
        app.MapPost("api/payment-methods",
            [Authorize(AuthenticationSchemes = Jwt)] async (AddPaymentMethodRequest request, ClaimsPrincipal user, ShopperService shoppers) =>
            {
                var created = await shoppers.AddMethodAsync(user.Caller(), request);
                return Results.Created($"api/payment-methods/{created.Id}", created);
            })
            .Produces<PaymentMethodDto>(StatusCodes.Status201Created)
            .WithTags("PaymentMethodEndpoints");

        app.MapGet("api/payment-methods",
            [Authorize(AuthenticationSchemes = Jwt)] async (ClaimsPrincipal user, ShopperService shoppers) =>
                Results.Ok(await shoppers.ListMethodsAsync(user.Caller())))
            .Produces<PaymentMethodDto[]>()
            .WithTags("PaymentMethodEndpoints");

        app.MapDelete("api/payment-methods/{paymentMethodId}",
            [Authorize(AuthenticationSchemes = Jwt)] async (string paymentMethodId, ClaimsPrincipal user, ShopperService shoppers) =>
            {
                await shoppers.RemoveMethodAsync(user.Caller(), paymentMethodId);
                return Results.NoContent();
            })
            .WithTags("PaymentMethodEndpoints");

        // orders
        app.MapPost("api/orders",
            [Authorize(AuthenticationSchemes = Jwt)] async (CreateOrderRequest request, ClaimsPrincipal user, OrderService orders) =>
            {
                var created = await orders.CreateAsync(user.Caller(), request);
                return Results.Created($"api/orders/{created.Id}", created);
            })
            .Produces<OrderDto>(StatusCodes.Status201Created)
            .WithTags("OrderEndpoints");

        app.MapGet("api/orders/{orderId}",
            [Authorize(AuthenticationSchemes = Jwt)] async (string orderId, ClaimsPrincipal user, OrderService orders) =>
                Results.Ok(await orders.GetAsync(user.Caller(), OrderId(orderId))))
            .Produces<OrderDto>()
            .WithTags("OrderEndpoints");

        app.MapPost("api/orders/{orderId}/checkout",
            [Authorize(AuthenticationSchemes = Jwt)] async (string orderId, PaymentMethodChoice request, ClaimsPrincipal user, OrderService orders) =>
                Results.Ok(await orders.CheckoutAsync(user.Caller(), OrderId(orderId), request)))
            .Produces<CheckoutDto>()
            .WithTags("OrderEndpoints");

        app.MapPost("api/orders/{orderId}/pay",
            [Authorize(AuthenticationSchemes = Jwt)] async (string orderId, PaymentMethodChoice request, ClaimsPrincipal user, OrderService orders) =>
                Results.Ok(await orders.PayAsync(user.Caller(), OrderId(orderId), request)))
            .Produces<OrderDto>()
            .WithTags("OrderEndpoints");

        // account
        app.MapPost("api/account/close",
            [Authorize(AuthenticationSchemes = Jwt)] async (ClaimsPrincipal user, ShopperService shoppers) =>
            {
                await shoppers.CloseAccountAsync(user.Caller());
                return Results.Ok(new { closed = true });
            })
            .WithTags("AccountEndpoints");
    }

    private static Guid OrderId(string id) =>
        Guid.TryParse(id, out var g) ? g : throw PaymentException.NotFound("No such order.");
}
