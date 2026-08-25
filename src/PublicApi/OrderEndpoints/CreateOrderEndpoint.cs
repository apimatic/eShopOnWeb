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
/// Places an order from catalog items for the signed-in shopper. The order starts awaiting payment -
/// see PayOrderEndpoint to authorize payment for it.
/// </summary>
public class CreateOrderEndpoint : IEndpoint<IResult, CreateOrderRequest, IOrderPaymentService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async (CreateOrderRequest request, ClaimsPrincipal user, IOrderPaymentService orderPaymentService) =>
            {
                request.BuyerId = user.Identity!.Name!;
                return await HandleAsync(request, orderPaymentService);
            })
            .Produces<CreateOrderResponse>()
            .WithTags("OrderEndpoints");
    }

    public async Task<IResult> HandleAsync(CreateOrderRequest request, IOrderPaymentService orderPaymentService)
    {
        var response = new CreateOrderResponse(request.CorrelationId());

        var items = request.Items.Select(i => new OrderItemQuantity(i.CatalogItemId, i.Quantity)).ToList();
        var shipToAddress = new Address(request.ShipToStreet, request.ShipToCity, request.ShipToState, request.ShipToCountry, request.ShipToZipCode);

        var order = await orderPaymentService.PlaceOrderAsync(request.BuyerId, items, shipToAddress);

        response.OrderId = order.Id;
        response.Order = order.ToDto();
        return Results.Created($"api/orders/{order.Id}", response);
    }
}
