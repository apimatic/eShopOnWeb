using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Threading;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderNotificationAggregate;
using Microsoft.eShopWeb.Infrastructure.Messaging;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.OrderNotificationEndpoints;

public sealed class CreateOrderEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders",
                [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async (
                    CreateOrderRequest request,
                    ClaimsPrincipal principal,
                    OrderNotificationService service,
                    CancellationToken cancellationToken) =>
                {
                    if (!EndpointResults.TryGetBuyerId(principal, out var buyerId))
                    {
                        return Results.Unauthorized();
                    }

                    if (request.ShippingAddress is null)
                    {
                        return EndpointResults.BadRequest("A shipping address is required.");
                    }

                    if (request.Items is null)
                    {
                        return EndpointResults.BadRequest("At least one catalog item with a positive quantity is required.");
                    }

                    try
                    {
                        var lines = request.Items
                            .Select(item => new OrderLineInput(item.CatalogItemId, item.Quantity))
                            .ToList();
                        var address = new ShippingAddressInput(
                            request.ShippingAddress.Street,
                            request.ShippingAddress.City,
                            request.ShippingAddress.State,
                            request.ShippingAddress.Country,
                            request.ShippingAddress.ZipCode);
                        var orderId = await service.PlaceOrderAsync(buyerId, lines, address, cancellationToken);
                        return Results.Created($"/api/orders/{orderId}", new CreateOrderResponse(orderId));
                    }
                    catch (InvalidOrderRequestException ex)
                    {
                        return EndpointResults.BadRequest(ex.Message);
                    }
                    catch (OverflowException)
                    {
                        return EndpointResults.BadRequest("The requested item quantity is too large.");
                    }
                })
            .Produces<CreateOrderResponse>(StatusCodes.Status201Created)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .WithTags("OrderEndpoints");
    }
}

public sealed class DispatchOrderEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders/{orderId:int}/dispatch",
                [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS,
                    AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async (
                    int orderId,
                    OrderNotificationService service,
                    CancellationToken cancellationToken) =>
                {
                    var result = await service.DispatchOrderAsync(orderId, cancellationToken);
                    return result switch
                    {
                        OrderTransitionResult.Success => Results.Ok(new OrderTransitionResponse(orderId, "dispatched")),
                        OrderTransitionResult.NotFound => Results.NotFound(),
                        OrderTransitionResult.Conflict => EndpointResults.Conflict("The order cannot be dispatched from its current state."),
                        _ => throw new ArgumentOutOfRangeException(nameof(result), result, "Unknown order transition result.")
                    };
                })
            .Produces<OrderTransitionResponse>()
            .Produces(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .WithTags("OrderEndpoints");
    }
}

public sealed class CancelOrderEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders/{orderId:int}/cancel",
                [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS,
                    AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async (
                    int orderId,
                    OrderNotificationService service,
                    CancellationToken cancellationToken) =>
                {
                    var result = await service.CancelOrderAsync(orderId, cancellationToken);
                    return result switch
                    {
                        OrderTransitionResult.Success => Results.Ok(new OrderTransitionResponse(orderId, "cancelled")),
                        OrderTransitionResult.NotFound => Results.NotFound(),
                        OrderTransitionResult.Conflict => EndpointResults.Conflict("The order cannot be cancelled from its current state."),
                        _ => throw new ArgumentOutOfRangeException(nameof(result), result, "Unknown order transition result.")
                    };
                })
            .Produces<OrderTransitionResponse>()
            .Produces(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .WithTags("OrderEndpoints");
    }
}

public sealed class ListMyOrdersEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/my-orders",
                [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async (
                    ClaimsPrincipal principal,
                    OrderNotificationService service,
                    CancellationToken cancellationToken) =>
                {
                    if (!EndpointResults.TryGetBuyerId(principal, out var buyerId))
                    {
                        return Results.Unauthorized();
                    }

                    var orders = await service.GetOrdersAsync(buyerId, cancellationToken);
                    var result = new List<OrderDto>(orders.Count);
                    foreach (var order in orders)
                    {
                        var notifications = await service.GetOrderNotificationsAsync(buyerId, order.Id, cancellationToken)
                                            ?? new List<OrderNotification>();
                        result.Add(OrderNotificationDtoMapper.ToDto(order, notifications));
                    }

                    return Results.Ok(new MyOrdersResponse(result));
                })
            .Produces<MyOrdersResponse>()
            .WithTags("OrderEndpoints");
    }
}

public sealed class ListOrderNotificationsEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/orders/{orderId:int}/notifications",
                [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async (
                    int orderId,
                    ClaimsPrincipal principal,
                    OrderNotificationService service,
                    CancellationToken cancellationToken) =>
                {
                    if (!EndpointResults.TryGetBuyerId(principal, out var buyerId))
                    {
                        return Results.Unauthorized();
                    }

                    var notifications = await service.GetOrderNotificationsAsync(buyerId, orderId, cancellationToken);
                    return notifications is null
                        ? Results.NotFound()
                        : Results.Ok(new OrderNotificationsResponse(
                            orderId,
                            notifications.Select(OrderNotificationDtoMapper.ToDto).ToList()));
                })
            .Produces<OrderNotificationsResponse>()
            .Produces(StatusCodes.Status404NotFound)
            .WithTags("OrderEndpoints");
    }
}
