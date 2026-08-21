using System;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
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
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
            async (CreateOrderRequest request, HttpContext httpContext, ICatalogOrderService orders) =>
            {
                return await HandleAsync(request, httpContext, orders);
            })
            .Produces<CreateOrderResponse>(StatusCodes.Status201Created)
            .Produces(StatusCodes.Status400BadRequest)
            .WithTags("OrderEndpoints");
    }

    public Task<IResult> HandleAsync(CreateOrderRequest request, ICatalogOrderService orders)
    {
        throw new NotSupportedException();
    }

    public async Task<IResult> HandleAsync(CreateOrderRequest request, HttpContext httpContext, ICatalogOrderService orders)
    {
        try
        {
            var items = request.Items.Select(i => new CatalogOrderItem(i.CatalogItemId, i.Quantity)).ToList();
            var order = await orders.PlaceAsync(httpContext.GetBuyerId(), items);
            await _notifications.NotifyOrderPlacedAsync(order);
            var response = new CreateOrderResponse(request.CorrelationId())
            {
                OrderId = order.Id,
                Status = order.Status.ToString()
            };
            return Results.Created($"api/orders/{order.Id}", response);
        }
        catch (ArgumentException ex)
        {
            return Results.BadRequest(new { message = ex.Message });
        }
    }
}
