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

public class CreateOrderEndpoint : IEndpoint<IResult, CreateOrderApiRequest, HttpContext>
{
    private readonly IOrderCheckoutService _checkout;

    public CreateOrderEndpoint(IOrderCheckoutService checkout)
    {
        _checkout = checkout;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
            async (CreateOrderApiRequest request, HttpContext httpContext) => await HandleAsync(request, httpContext))
            .Produces<CreateOrderResponse>(StatusCodes.Status201Created)
            .WithTags("OrderEndpoints");
    }

    public async Task<IResult> HandleAsync(CreateOrderApiRequest request, HttpContext httpContext)
    {
        var order = await _checkout.PlaceOrderAsync(new PlaceOrderRequest
        {
            BuyerId = httpContext.GetBuyerId(),
            Items = (request.Items ?? []).Select(i => new PlaceOrderItem
            {
                CatalogItemId = i.CatalogItemId,
                Quantity = i.Quantity
            }).ToList(),
            ShipTo = OrderDtoMapper.ToAddress(request.ShipTo)
        });

        var response = new CreateOrderResponse
        {
            OrderId = order.Id,
            Order = OrderDtoMapper.ToDto(order)
        };

        return Results.Created($"api/orders/{order.Id}", response);
    }
}
