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

public class CreateOrderEndpoint : IEndpoint<IResult, CreateOrderRequest, IOrderPaymentService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
            async (CreateOrderRequest request, IOrderPaymentService service, ClaimsPrincipal user) =>
            {
                request.BuyerId = user.RequireBuyerId();
                return await HandleAsync(request, service);
            })
            .Produces<CreateOrderResponse>(StatusCodes.Status201Created)
            .WithTags("OrderEndpoints");
    }

    public async Task<IResult> HandleAsync(CreateOrderRequest request, IOrderPaymentService service)
    {
        Address? address = null;
        if (request.ShippingAddress is not null)
        {
            var shipping = request.ShippingAddress;
            address = new Address(
                shipping.Street ?? "123 Main St.",
                shipping.City ?? "Kent",
                shipping.State ?? "OH",
                shipping.Country ?? "United States",
                shipping.ZipCode ?? "44240");
        }

        var lines = request.Items.Select(item => new OrderLineRequest
        {
            CatalogItemId = item.CatalogItemId,
            Quantity = item.Quantity
        }).ToList();

        var order = await service.PlaceOrderAsync(request.BuyerId, lines, address);
        var response = new CreateOrderResponse
        {
            OrderId = order.Id,
            Order = PaymentResponseMapper.ToDto(order)
        };
        return Results.Created($"api/orders/{order.Id}", response);
    }
}
