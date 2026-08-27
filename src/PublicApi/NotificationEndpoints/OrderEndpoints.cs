using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using BlazorShared.Authorization;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.NotificationEndpoints;

public sealed class OrderEndpoints : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (PlaceOrderRequest request, HttpContext httpContext, IOrderNotificationService service, CancellationToken cancellationToken) =>
                await NotificationEndpointResults.ExecuteAsync(async () =>
                {
                    if (request.Items is null || request.ShippingAddress is null)
                    {
                        throw new NotificationValidationException("items and shippingAddress are required.");
                    }

                    var command = new PlaceOrderCommand(
                        request.Items.Select(x => new PlaceOrderItem(x.CatalogItemId, x.Quantity)).ToList(),
                        new ShippingAddress(
                            request.ShippingAddress.Street,
                            request.ShippingAddress.City,
                            request.ShippingAddress.State,
                            request.ShippingAddress.Country,
                            request.ShippingAddress.ZipCode));
                    int orderId = await service.PlaceOrderAsync(Buyer(httpContext), command, cancellationToken);
                    return Results.Created($"/api/orders/{orderId}", new { orderId });
                }))
            .Produces(StatusCodes.Status201Created)
            .Produces(StatusCodes.Status400BadRequest)
            .Produces(StatusCodes.Status401Unauthorized)
            .WithTags("Orders");

        app.MapPost("api/orders/{orderId:int}/dispatch",
            [Authorize(Roles = Constants.Roles.ADMINISTRATORS, AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int orderId, IOrderNotificationService service, CancellationToken cancellationToken) =>
                await NotificationEndpointResults.ExecuteAsync(async () =>
                {
                    await service.DispatchOrderAsync(orderId, cancellationToken);
                    return Results.Ok(new { orderId, status = "Dispatched" });
                }))
            .Produces(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status403Forbidden)
            .Produces(StatusCodes.Status404NotFound)
            .WithTags("Orders");

        app.MapPost("api/orders/{orderId:int}/cancel",
            [Authorize(Roles = Constants.Roles.ADMINISTRATORS, AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int orderId, IOrderNotificationService service, CancellationToken cancellationToken) =>
                await NotificationEndpointResults.ExecuteAsync(async () =>
                {
                    await service.CancelOrderAsync(orderId, cancellationToken);
                    return Results.Ok(new { orderId, status = "Cancelled" });
                }))
            .Produces(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status403Forbidden)
            .Produces(StatusCodes.Status404NotFound)
            .WithTags("Orders");

        app.MapGet("api/my-orders",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (HttpContext httpContext, IOrderNotificationService service, CancellationToken cancellationToken) =>
            {
                IReadOnlyList<OrderDto> orders = await service.GetMyOrdersAsync(Buyer(httpContext), cancellationToken);
                return Results.Ok(new { orders });
            })
            .Produces(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status401Unauthorized)
            .WithTags("Orders");

        app.MapGet("api/orders/{orderId:int}/notifications",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int orderId, HttpContext httpContext, IOrderNotificationService service, CancellationToken cancellationToken) =>
            {
                IReadOnlyList<NotificationDto>? notifications = await service.GetOrderNotificationsAsync(
                    Buyer(httpContext), orderId, cancellationToken);
                return notifications is null ? Results.NotFound() : Results.Ok(new { orderId, notifications });
            })
            .Produces(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status404NotFound)
            .WithTags("Orders");
    }

    private static string Buyer(HttpContext context) => context.User.Identity?.Name ?? string.Empty;
}

public sealed record PlaceOrderRequest(IReadOnlyList<PlaceOrderItemRequest>? Items, ShippingAddressRequest? ShippingAddress);
public sealed record PlaceOrderItemRequest(int CatalogItemId, int Quantity);
public sealed record ShippingAddressRequest(string Street, string City, string State, string Country, string ZipCode);
