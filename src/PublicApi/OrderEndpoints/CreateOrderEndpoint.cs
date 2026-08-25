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
/// Places an order directly from catalog items (no basket involved). The order starts out awaiting payment.
/// </summary>
public class CreateOrderEndpoint : IEndpoint<IResult, CreateOrderRequest, IOrderCheckoutService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (CreateOrderRequest request, ClaimsPrincipal user, IOrderCheckoutService orderCheckoutService) =>
            {
                request.BuyerId = user.Identity!.Name!;
                return await HandleAsync(request, orderCheckoutService);
            })
            .Produces<CreateOrderResponse>()
            .WithTags("OrderEndpoints");
    }

    public async Task<IResult> HandleAsync(CreateOrderRequest request, IOrderCheckoutService orderCheckoutService)
    {
        var response = new CreateOrderResponse(request.CorrelationId());

        var shipTo = new Address(request.ShipToAddress.Street, request.ShipToAddress.City,
            request.ShipToAddress.State, request.ShipToAddress.Country, request.ShipToAddress.ZipCode);

        var items = request.Items.Select(i => new OrderItemRequest(i.CatalogItemId, i.Quantity)).ToList();

        var order = await orderCheckoutService.PlaceOrderAsync(request.BuyerId, shipTo, items);

        response.OrderId = order.Id;
        response.Order = OrderMapping.ToDto(order);
        return Results.Created($"api/orders/{order.Id}", response);
    }
}
