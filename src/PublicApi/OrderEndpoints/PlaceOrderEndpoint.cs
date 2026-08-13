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
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.PublicApi.Extensions;
using Microsoft.eShopWeb.PublicApi.NotificationEndpoints;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

/// <summary>
/// POST /api/orders — place an order from catalog items for the signed-in shopper (identity comes from the
/// token), reusing the app's existing order/order-item model. The shopper is told their order was placed.
/// </summary>
public class PlaceOrderEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async (
                PlaceOrderRequest request,
                ClaimsPrincipal user,
                IOrderNotificationService service,
                CancellationToken cancellationToken) =>
            {
                var buyerId = user.GetUserName();
                if (string.IsNullOrEmpty(buyerId))
                {
                    return Results.Unauthorized();
                }

                if (request?.Items is null || request.Items.Count == 0)
                {
                    return Results.BadRequest(new { message = "An order must contain at least one item." });
                }

                if (request.Items.Any(i => i.Quantity <= 0))
                {
                    return Results.BadRequest(new { message = "Every item quantity must be greater than zero." });
                }

                var lines = request.Items.Select(i => new OrderLineItem(i.CatalogItemId, i.Quantity)).ToList();

                try
                {
                    var order = await service.PlaceOrderAsync(buyerId, lines, cancellationToken);

                    // Surface the "order placed" notification (if the shopper had a number on file).
                    var notifications = await service.GetNotificationsForOrderAsync(order.Id, refreshFromProvider: false, cancellationToken);
                    var placed = notifications?.FirstOrDefault();

                    var response = new PlaceOrderResponse
                    {
                        OrderId = order.Id,
                        OrderDate = order.OrderDate,
                        Total = order.Total(),
                        Items = order.OrderItems.Select(OrderLineDto.From).ToList(),
                        Notification = placed is null ? null : NotificationDto.From(placed)
                    };
                    return Results.Created($"api/orders/{order.Id}", response);
                }
                catch (ArgumentException ex)
                {
                    return Results.BadRequest(new { message = ex.Message });
                }
            })
            .Produces<PlaceOrderResponse>(StatusCodes.Status201Created)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .WithTags("OrderEndpoints");
    }
}
