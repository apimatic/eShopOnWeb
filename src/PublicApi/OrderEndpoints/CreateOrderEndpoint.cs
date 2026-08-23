using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.PublicApi;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

public class CreateOrderEndpoint : IEndpoint<IResult, CreateOrderRequest, IOrderPaymentService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (CreateOrderRequest request, IOrderPaymentService service, HttpContext httpContext) =>
            {
                var buyerId = BuyerIdentity.GetBuyerId(httpContext);
                if (string.IsNullOrEmpty(buyerId))
                {
                    return Results.Unauthorized();
                }

                request.BuyerId = buyerId;
                return await HandleAsync(request, service);
            })
            .Produces<OrderResponse>(StatusCodes.Status201Created)
            .WithTags("OrderEndpoints");
    }

    public async Task<IResult> HandleAsync(CreateOrderRequest request, IOrderPaymentService service)
    {
        var items = request.Items.Select(i => new CreatePaidOrderItem(i.CatalogItemId, i.Quantity)).ToList();
        var shipTo = request.ShipTo is null
            ? null
            : new ShipToAddressDto(request.ShipTo.Street, request.ShipTo.City, request.ShipTo.State, request.ShipTo.Country, request.ShipTo.ZipCode);

        var order = await service.CreateOrderAsync(request.BuyerId, items, shipTo);
        var response = OrderResponseMapper.ToResponse(order, request.CorrelationId());
        return Results.Created($"api/orders/{order.Id}", response);
    }
}
