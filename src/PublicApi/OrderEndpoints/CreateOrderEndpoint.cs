using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Payments;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

public class CreateOrderEndpoint : IEndpoint<IResult, CreateOrderRequest, IOrderCheckoutService>
{
    private readonly IPayPalGateway _payPal;

    public CreateOrderEndpoint(IPayPalGateway payPal)
    {
        _payPal = payPal;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
            async (CreateOrderRequest request, IOrderCheckoutService checkout, HttpContext httpContext) =>
            {
                request.BuyerId = httpContext.User.GetBuyerId();
                return await HandleAsync(request, checkout);
            })
            .Produces<OrderResponse>(StatusCodes.Status201Created)
            .WithTags("OrderEndpoints");
    }

    public async Task<IResult> HandleAsync(CreateOrderRequest request, IOrderCheckoutService checkout)
    {
        var lines = (request.Items ?? []).Select(i => new OrderLineRequest(i.CatalogItemId, i.Quantity)).ToList();
        var order = await checkout.PlaceOrderAsync(request.BuyerId!, lines, request.ShipTo?.ToAddress());
        var response = OrderResponseMapper.From(order, _payPal.Currency);
        return Results.Created($"api/orders/{response.OrderId}", response);
    }
}
