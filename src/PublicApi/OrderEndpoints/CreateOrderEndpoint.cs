using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

public class CreateOrderEndpoint : IEndpoint<IResult, CreateOrderRequest, IOrderService>
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
            (CreateOrderRequest request, IOrderService orderService, HttpContext httpContext) =>
            {
                request.BuyerId = httpContext.GetBuyerId();
                return await HandleAsync(request, orderService);
            })
            .Produces<CreateOrderResponse>(StatusCodes.Status201Created)
            .Produces(StatusCodes.Status400BadRequest)
            .WithTags("OrderEndpoints");
    }

    public async Task<IResult> HandleAsync(CreateOrderRequest request, IOrderService orderService)
    {
        var items = (request.Items ?? new List<CreateOrderItemRequest>())
            .Select(i => new CatalogQuantity(i.CatalogItemId, i.Quantity))
            .ToList();

        var shipTo = request.ShipTo is null
            ? new Address("123 Main Street", "Seattle", "WA", "USA", "98101")
            : new Address(
                request.ShipTo.Street,
                request.ShipTo.City,
                request.ShipTo.State,
                request.ShipTo.Country,
                request.ShipTo.ZipCode);

        var order = await orderService.CreateOrderFromCatalogItemsAsync(request.BuyerId, items, shipTo);
        await _notifications.NotifyOrderPlacedAsync(order);

        var response = new CreateOrderResponse(request.CorrelationId())
        {
            OrderId = order.Id,
            Status = "pending"
        };
        return Results.Created($"api/orders/{order.Id}", response);
    }
}
