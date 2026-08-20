using System;
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

public class PlaceOrderEndpoint : IEndpoint<IResult, PlaceOrderRequest, IOrderService>
{
    private readonly IOrderNotificationService _notifications;

    public PlaceOrderEndpoint(IOrderNotificationService notifications)
    {
        _notifications = notifications;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
            async (PlaceOrderRequest request, HttpContext http, IOrderService orders) =>
            {
                return await HandleAsync(request, http, orders);
            })
            .Produces<PlaceOrderResponse>(StatusCodes.Status201Created)
            .Produces(StatusCodes.Status400BadRequest)
            .WithTags("OrderEndpoints");
    }

    public Task<IResult> HandleAsync(PlaceOrderRequest request, IOrderService orders)
        => HandleAsync(request, null!, orders);

    private async Task<IResult> HandleAsync(PlaceOrderRequest request, HttpContext http, IOrderService orders)
    {
        var items = (request.Items ?? new()).Select(i => (i.CatalogItemId, i.Quantity)).ToList();
        var address = new Address("123 Main St.", "Kent", "OH", "United States", "44240");
        var order = await orders.PlaceOrderAsync(http.GetRequiredBuyerId(), items, address);

        try
        {
            await _notifications.NotifyOrderPlacedAsync(order);
        }
        catch (Exception)
        {
            // Placing the order must succeed even if messaging fails.
        }

        var response = new PlaceOrderResponse(request.CorrelationId())
        {
            OrderId = order.Id,
            Status = order.Status.ToString()
        };
        return Results.Created($"api/orders/{order.Id}", response);
    }
}
