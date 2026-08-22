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

public class CreateOrderEndpoint : IEndpoint<IResult, CreateOrderRequest, ICheckoutPaymentService>
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
            (CreateOrderRequest request, ICheckoutPaymentService checkout) =>
            {
                return await HandleAsync(request, checkout);
            })
            .Produces<CreateOrderResponse>(StatusCodes.Status201Created)
            .WithTags("OrderEndpoints");
    }

    public async Task<IResult> HandleAsync(CreateOrderRequest request, ICheckoutPaymentService checkout)
    {
        var buyerId = _httpContextAccessor.HttpContext?.User.Identity?.Name ?? string.Empty;
        var address = request.ShipTo is null
            ? new Address("123 Main St.", "Kent", "OH", "United States", "44240")
            : new Address(request.ShipTo.Street, request.ShipTo.City, request.ShipTo.State, request.ShipTo.Country, request.ShipTo.ZipCode);

        var lines = request.Items.Select(i => new OrderLineRequest(i.CatalogItemId, i.Quantity)).ToList();
        var order = await checkout.PlaceOrderAsync(buyerId, lines, address, default);

        var response = new CreateOrderResponse
        {
            OrderId = order.Id,
            Order = OrderResponseMapper.Map(order)
        };
        return Results.Created($"api/orders/{order.Id}", response);
    }
}
