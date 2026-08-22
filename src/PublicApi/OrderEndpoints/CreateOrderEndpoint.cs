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

public class CreateOrderEndpoint : IEndpoint<IResult, CreateOrderRequest, IOrderPaymentService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (CreateOrderRequest request, IOrderPaymentService service, HttpContext http) =>
            {
                request.BuyerId = http.User.Identity?.Name;
                return await HandleAsync(request, service);
            })
            .Produces<CreateOrderResponse>()
            .WithTags("OrderEndpoints");
    }

    public async Task<IResult> HandleAsync(CreateOrderRequest request, IOrderPaymentService service)
    {
        var shipping = request.ShipToAddress;
        var address = new ShippingAddressRequest(
            shipping?.Street ?? "123 Main St",
            shipping?.City ?? "San Jose",
            shipping?.State ?? "CA",
            shipping?.Country ?? "US",
            shipping?.ZipCode ?? "95131");

        var items = request.Items
            .Select(i => new OrderLineRequest(i.CatalogItemId, i.Quantity))
            .ToList();

        var order = await service.PlaceOrderAsync(request.BuyerId ?? string.Empty, items, address, default);
        var dto = OrderDto.From(order);
        var response = new CreateOrderResponse
        {
            OrderId = dto.OrderId,
            Order = dto
        };
        return Results.Created($"api/orders/{dto.OrderId}", response);
    }
}
