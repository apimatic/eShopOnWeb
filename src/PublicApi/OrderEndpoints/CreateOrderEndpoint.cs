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

public class CreateOrderEndpoint : IEndpoint<IResult, CreateOrderApiRequest, IOrderPaymentService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
            async (CreateOrderApiRequest request, IOrderPaymentService service, HttpContext http) =>
            {
                request.BuyerId = http.RequireBuyerId();
                return await HandleAsync(request, service);
            })
            .Produces<CreateOrderApiResponse>(StatusCodes.Status201Created)
            .WithTags("OrderEndpoints");
    }

    public async Task<IResult> HandleAsync(CreateOrderApiRequest request, IOrderPaymentService service)
    {
        var order = await service.PlaceOrderAsync(new PlaceOrderRequest(
            request.BuyerId!,
            request.Items.Select(i => new PlaceOrderItem(i.CatalogItemId, i.Quantity)).ToList(),
            request.ShippingAddress?.ToAddress()));

        var response = new CreateOrderApiResponse
        {
            OrderId = order.Id,
            Order = OrderDtoMapper.ToDto(order)
        };
        return Results.Created($"api/orders/{order.Id}", response);
    }
}
