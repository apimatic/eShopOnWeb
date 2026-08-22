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
    private readonly IOrderSmsNotificationService _notifications;

    public CreateOrderEndpoint(IOrderSmsNotificationService notifications)
    {
        _notifications = notifications;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
            async (CreateOrderRequest request, HttpContext httpContext, IOrderService orderService) =>
            {
                request.BuyerId = httpContext.GetRequiredBuyerId();
                return await HandleAsync(request, orderService);
            })
            .Produces<CreateOrderResponse>(StatusCodes.Status201Created)
            .Produces(StatusCodes.Status400BadRequest)
            .WithTags("OrderEndpoints");
    }

    public async Task<IResult> HandleAsync(CreateOrderRequest request, IOrderService orderService)
    {
        var shipTo = request.ShipTo;
        var address = new Address(
            shipTo?.Street ?? "123 Main St.",
            shipTo?.City ?? "Kent",
            shipTo?.State ?? "OH",
            shipTo?.Country ?? "United States",
            shipTo?.ZipCode ?? "44240");

        var lines = request.Items.Select(i => (i.CatalogItemId, i.Quantity)).ToList();
        var order = await orderService.CreateOrderFromItemsAsync(request.BuyerId, lines, address);
        await _notifications.NotifyOrderPlacedAsync(order);

        var response = CreateOrderResponse.From(order, request.CorrelationId());
        return Results.Created($"api/orders/{response.OrderId}", response);
    }
}
