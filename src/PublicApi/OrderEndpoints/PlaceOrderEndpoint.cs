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
/// Places an order from catalog item ids and quantities. The order starts awaiting payment -
/// call PayOrderEndpoint next to authorize a hold for its total.
/// </summary>
public class PlaceOrderEndpoint : IEndpoint<IResult, PlaceOrderRequest, IOrderPaymentService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (PlaceOrderRequestBody body, ClaimsPrincipal user, IOrderPaymentService orderPaymentService) =>
            {
                var request = new PlaceOrderRequest(user.Identity?.Name ?? string.Empty, body.Items, body.ShipToAddress);
                return await HandleAsync(request, orderPaymentService);
            })
            .Produces<PlaceOrderResponse>()
            .WithTags("OrderEndpoints");
    }

    public async Task<IResult> HandleAsync(PlaceOrderRequest request, IOrderPaymentService orderPaymentService)
    {
        var response = new PlaceOrderResponse(request.CorrelationId());

        var items = request.Items.Select(i => new OrderItemRequest(i.CatalogItemId, i.Quantity)).ToList();
        var addr = request.ShipToAddress;
        var shipToAddress = addr is null
            ? new Address("123 Main St.", "Kent", "OH", "United States", "44240")
            : new Address(addr.Street, addr.City, addr.State, addr.Country, addr.ZipCode);

        var order = await orderPaymentService.PlaceOrderAsync(request.BuyerId, items, shipToAddress);

        response.OrderId = order.Id;
        response.Order = OrderMapper.ToDto(order);
        return Results.Created($"api/orders/{order.Id}", response);
    }
}
