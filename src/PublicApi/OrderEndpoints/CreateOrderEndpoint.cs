using System.Collections.Generic;
using System.Linq;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Services;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

public class CreateOrderEndpoint : IEndpoint<IResult, CreateOrderRequest, ICatalogOrderService>
{
    private readonly IOrderNotificationService _notifications;

    public CreateOrderEndpoint(IOrderNotificationService notifications)
    {
        _notifications = notifications;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (CreateOrderRequest request, ICatalogOrderService orders, HttpContext httpContext) =>
            {
                return await HandleAsync(request, orders, httpContext);
            })
            .Produces<CreateOrderResponse>(StatusCodes.Status201Created)
            .Produces(StatusCodes.Status400BadRequest)
            .WithTags("OrderEndpoints");
    }

    public Task<IResult> HandleAsync(CreateOrderRequest request, ICatalogOrderService orders)
        => HandleAsync(request, orders, null!);

    private async Task<IResult> HandleAsync(CreateOrderRequest request, ICatalogOrderService orders, HttpContext httpContext)
    {
        var buyerId = httpContext.GetBuyerId();
        if (string.IsNullOrEmpty(buyerId))
        {
            return Results.Unauthorized();
        }

        var lines = (request.Items ?? new List<CreateOrderItemRequest>())
            .Select(i => new CatalogOrderLine(i.CatalogItemId, i.Quantity))
            .ToList();

        Address? shipTo = null;
        if (request.ShipTo is not null)
        {
            shipTo = new Address(
                request.ShipTo.Street,
                request.ShipTo.City,
                request.ShipTo.State,
                request.ShipTo.Country,
                request.ShipTo.ZipCode);
        }

        var order = await orders.PlaceAsync(buyerId, lines, shipTo, httpContext.RequestAborted);
        await _notifications.NotifyOrderPlacedAsync(order, httpContext.RequestAborted);

        var response = new CreateOrderResponse
        {
            OrderId = order.Id,
            Status = order.FulfillmentStatus.ToString()
        };
        return Results.Created($"api/orders/{order.Id}", response);
    }
}
