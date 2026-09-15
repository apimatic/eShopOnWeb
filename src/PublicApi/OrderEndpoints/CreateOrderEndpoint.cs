using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

public class CreateOrderEndpoint : IEndpoint<IResult, CreateOrderRequest, ICheckoutService>
{
    private readonly IHttpContextAccessor _httpContextAccessor;

    public CreateOrderEndpoint(IHttpContextAccessor httpContextAccessor)
    {
        _httpContextAccessor = httpContextAccessor;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (CreateOrderRequest request, ICheckoutService checkout) =>
            {
                return await HandleAsync(request, checkout);
            })
            .Produces<CreateOrderResponse>()
            .WithTags("OrderEndpoints");
    }

    public async Task<IResult> HandleAsync(CreateOrderRequest request, ICheckoutService checkout)
    {
        var http = _httpContextAccessor.HttpContext!;
        var lines = (request.Items ?? new()).Select(i => new CatalogOrderLine
        {
            CatalogItemId = i.CatalogItemId,
            Quantity = i.Quantity
        }).ToList();

        var order = await checkout.CreateOrderAsync(http.RequireBuyerId(), lines, OrderDtoMapper.ToAddress(request.ShipTo));
        var response = new CreateOrderResponse
        {
            OrderId = order.Id,
            Order = OrderDtoMapper.ToDto(order)
        };
        return Results.Created($"api/orders/{order.Id}", response);
    }
}
