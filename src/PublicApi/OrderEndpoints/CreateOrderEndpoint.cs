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

public class CreateOrderEndpoint : IEndpoint<IResult, CreateOrderRequest, IPaymentCheckoutService>
{
    private readonly IHttpContextAccessor _httpContextAccessor;

    public CreateOrderEndpoint(IHttpContextAccessor httpContextAccessor)
    {
        _httpContextAccessor = httpContextAccessor;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
            async (CreateOrderRequest request, IPaymentCheckoutService payments) =>
            {
                return await HandleAsync(request, payments);
            })
            .Produces<OrderResponse>(StatusCodes.Status201Created)
            .WithTags("OrderEndpoints");
    }

    public async Task<IResult> HandleAsync(CreateOrderRequest request, IPaymentCheckoutService payments)
    {
        var order = await payments.PlaceOrderAsync(
            EndpointUser.BuyerId(_httpContextAccessor.HttpContext!),
            request.Items.Select(i => new OrderLineInput { CatalogItemId = i.CatalogItemId, Quantity = i.Quantity }).ToList(),
            OrderResponseMapper.ToAddress(request.ShipToAddress));

        return Results.Created($"api/orders/{order.Id}", OrderResponseMapper.Map(order, payments.Currency));
    }
}
