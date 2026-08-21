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

public class CreateOrderEndpoint : IEndpoint<IResult, CreateOrderRequest, IOrderCheckoutService>
{
    private readonly IHttpContextAccessor _httpContextAccessor;
    private readonly IPaymentSettings _paymentSettings;

    public CreateOrderEndpoint(IHttpContextAccessor httpContextAccessor, IPaymentSettings paymentSettings)
    {
        _httpContextAccessor = httpContextAccessor;
        _paymentSettings = paymentSettings;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (CreateOrderRequest request, IOrderCheckoutService checkoutService) =>
            {
                return await HandleAsync(request, checkoutService);
            })
            .Produces<CreateOrderResponse>(StatusCodes.Status201Created)
            .WithTags("OrderEndpoints");
    }

    public async Task<IResult> HandleAsync(CreateOrderRequest request, IOrderCheckoutService checkoutService)
    {
        var httpContext = _httpContextAccessor.HttpContext!;
        var buyerId = Caller.Name(httpContext);
        var address = new Address(
            request.ShipToAddress?.Street ?? "123 Main St.",
            request.ShipToAddress?.City ?? "Kent",
            request.ShipToAddress?.State ?? "OH",
            request.ShipToAddress?.Country ?? "US",
            request.ShipToAddress?.ZipCode ?? "44240");

        var lines = (request.Items ?? []).Select(i => new CatalogOrderLine(i.CatalogItemId, i.Quantity)).ToList();
        var order = await checkoutService.PlaceOrderAsync(buyerId, lines, address, httpContext.RequestAborted);

        var response = new CreateOrderResponse
        {
            OrderId = order.Id,
            Order = OrderDto.From(order, _paymentSettings.Currency)
        };

        return Results.Created($"api/orders/{order.Id}", response);
    }
}
