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

public class CreateOrderEndpoint : IEndpoint<IResult, CreateOrderRequest, IShopperOrderService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
            async (CreateOrderRequest request, IShopperOrderService orders, IOrderNotificationService notifications, HttpContext http) =>
            {
                return await HandleAsync(request, orders, notifications, http);
            })
            .Produces<CreateOrderResponse>(StatusCodes.Status201Created)
            .WithTags("OrderEndpoints");
    }

    public Task<IResult> HandleAsync(CreateOrderRequest request, IShopperOrderService orders)
    {
        throw new System.NotSupportedException("Use the routed handler that supplies the current request services.");
    }

    private static async Task<IResult> HandleAsync(CreateOrderRequest request, IShopperOrderService orders, IOrderNotificationService notifications, HttpContext http)
    {
        var buyerId = http.User.GetBuyerId();
        var shipTo = request.ShipTo ?? new CreateOrderAddressRequest();
        var address = new Address(shipTo.Street, shipTo.City, shipTo.State, shipTo.Country, shipTo.ZipCode);
        var items = request.Items.Select(i => new CatalogOrderItemRequest
        {
            CatalogItemId = i.CatalogItemId,
            Quantity = i.Quantity
        }).ToList();

        var order = await orders.PlaceOrderAsync(buyerId, items, address);
        var list = await notifications.ListForOrderAsync(order.Id, refreshFromProvider: false);
        var response = new CreateOrderResponse(request.CorrelationId())
        {
            OrderId = order.Id,
            Status = order.Status.ToString(),
            Total = order.Total(),
            Notifications = list.Select(OrderNotificationDtoMapper.ToDto).ToList()
        };

        return Results.Created($"api/orders/{order.Id}", response);
    }
}
