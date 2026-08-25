using System.Linq;
using System.Security.Claims;
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

/// <summary>
/// Places an order directly from catalog item ids/quantities for the signed-in shopper. The order
/// starts out awaiting payment — see <see cref="PayOrderEndpoint"/>.
/// </summary>
public class CreateOrderEndpoint : IEndpoint<IResult, CreateOrderRequest, IOrderService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (CreateOrderRequest request, ClaimsPrincipal user, IOrderService orderService) =>
            {
                request.BuyerId = user.Identity!.Name!;
                return await HandleAsync(request, orderService);
            })
            .Produces<CreateOrderResponse>()
            .WithTags("OrderEndpoints");
    }

    public async Task<IResult> HandleAsync(CreateOrderRequest request, IOrderService orderService)
    {
        var response = new CreateOrderResponse(request.CorrelationId());

        var address = request.ShippingAddress is { } a
            ? new Address(a.Street, a.City, a.State, a.Country, a.ZipCode)
            : new Address("123 Main St", "Redmond", "WA", "USA", "98052");

        var items = request.Items.Select(i => new OrderItemQuantity(i.CatalogItemId, i.Quantity)).ToList();

        var order = await orderService.CreateOrderFromCatalogItemsAsync(request.BuyerId, address, items);

        response.OrderId = order.Id;
        response.Order = OrderMapper.ToDto(order);
        return Results.Created($"api/my-orders/{order.Id}", response);
    }
}
