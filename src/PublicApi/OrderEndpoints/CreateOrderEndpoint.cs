using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

public class CreateOrderEndpoint : IEndpoint<IResult, CreateOrderRequest, IOrderCheckoutService>
{
    private readonly IHttpContextAccessor _httpContextAccessor;
    private readonly PayPalSettings _payPalSettings;

    public CreateOrderEndpoint(IHttpContextAccessor httpContextAccessor, PayPalSettings payPalSettings)
    {
        _httpContextAccessor = httpContextAccessor;
        _payPalSettings = payPalSettings;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (CreateOrderRequest request, IOrderCheckoutService checkout) =>
            {
                return await HandleAsync(request, checkout);
            })
            .Produces<OrderResponse>(StatusCodes.Status201Created)
            .WithTags("OrderEndpoints");
    }

    public async Task<IResult> HandleAsync(CreateOrderRequest request, IOrderCheckoutService checkout)
    {
        var buyerId = BuyerIdentity.RequireBuyerId(_httpContextAccessor.HttpContext!.User);
        var address = ToAddress(request.ShipToAddress);
        var lines = (request.Items ?? new()).ConvertAll(i => new OrderLineRequest(i.CatalogItemId, i.Quantity));
        var order = await checkout.PlaceOrderAsync(buyerId, lines, address);
        var response = OrderResponse.From(order, _payPalSettings.Currency);
        return Results.Created($"api/orders/{response.OrderId}", response);
    }

    private static Address ToAddress(AddressDto? dto)
    {
        return new Address(
            dto?.Street ?? "123 Test Street",
            dto?.City ?? "San Jose",
            dto?.State ?? "CA",
            dto?.Country ?? "US",
            dto?.ZipCode ?? "95131");
    }
}
