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
    private readonly IOrderNotificationService _notificationService;

    public CreateOrderEndpoint(IOrderNotificationService notificationService)
    {
        _notificationService = notificationService;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (CreateOrderRequest request, IOrderService orderService, HttpContext httpContext) =>
            {
                return await HandleAsync(request, orderService, httpContext);
            })
            .Produces<CreateOrderResponse>(StatusCodes.Status201Created)
            .Produces(StatusCodes.Status400BadRequest)
            .WithTags("OrderEndpoints");
    }

    public Task<IResult> HandleAsync(CreateOrderRequest request, IOrderService orderService)
        => HandleAsync(request, orderService, null!);

    private async Task<IResult> HandleAsync(CreateOrderRequest request, IOrderService orderService, HttpContext httpContext)
    {
        var address = request.ShipTo is null
            ? new Address("123 Main St.", "Kent", "OH", "United States", "44240")
            : new Address(request.ShipTo.Street, request.ShipTo.City, request.ShipTo.State, request.ShipTo.Country, request.ShipTo.ZipCode);

        var items = (request.Items ?? new List<CreateOrderItemRequest>())
            .Select(item => (item.CatalogItemId, item.Quantity))
            .ToList();
        var order = await orderService.CreateOrderFromItemsAsync(httpContext.GetRequiredBuyerId(), items, address);
        await _notificationService.NotifyOrderPlacedAsync(order);

        var response = new CreateOrderResponse(request.CorrelationId())
        {
            OrderId = order.Id,
            Status = order.Status.ToString()
        };

        return Results.Created($"api/orders/{order.Id}", response);
    }
}
