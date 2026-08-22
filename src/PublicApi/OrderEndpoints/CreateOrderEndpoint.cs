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
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (CreateOrderRequest request, HttpContext httpContext, IShopperOrderService shopperOrderService) =>
            {
                request.BuyerId = httpContext.GetBuyerId();
                return await HandleAsync(request, shopperOrderService);
            })
            .Produces<CreateOrderResponse>(StatusCodes.Status201Created)
            .Produces(StatusCodes.Status400BadRequest)
            .WithTags("OrderEndpoints");
    }

    public async Task<IResult> HandleAsync(CreateOrderRequest request, IShopperOrderService shopperOrderService)
    {
        Address? address = null;
        if (request.ShipToAddress is not null)
        {
            address = new Address(
                request.ShipToAddress.Street,
                request.ShipToAddress.City,
                request.ShipToAddress.State,
                request.ShipToAddress.Country,
                request.ShipToAddress.ZipCode);
        }

        var items = request.Items.Select(i => new CatalogOrderItemRequest(i.CatalogItemId, i.Quantity)).ToList();
        var result = await shopperOrderService.PlaceOrderAsync(request.BuyerId, items, address);
        var response = new CreateOrderResponse(request.CorrelationId())
        {
            OrderId = result.Order.Id,
            Status = result.Order.Status.ToString(),
            Total = result.Order.Total(),
            Notifications = result.Notifications.Select(OrderNotificationDto.From).ToList()
        };

        return Results.Created($"api/orders/{result.Order.Id}", response);
    }
}
