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

namespace Microsoft.eShopWeb.PublicApi.Payments;

public class CreateOrderEndpoint : IEndpoint<IResult, CreateOrderRequest, IPaymentService>
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
            (CreateOrderRequest request, IPaymentService paymentService) =>
            {
                return await HandleAsync(request, paymentService);
            })
            .Produces<OrderPlacementResult>()
            .WithTags("OrderEndpoints");
    }

    public async Task<IResult> HandleAsync(CreateOrderRequest request, IPaymentService paymentService)
    {
        var buyerId = _httpContextAccessor.HttpContext?.User?.Identity?.Name;
        if (string.IsNullOrWhiteSpace(buyerId))
        {
            return Results.Unauthorized();
        }

        var items = request.Items
            .Select(i => new OrderItemRequest(i.CatalogItemId, i.Quantity))
            .ToList();

        ShippingAddressDetails? shippingAddress = null;
        if (request.ShippingAddress is not null)
        {
            shippingAddress = new ShippingAddressDetails(
                request.ShippingAddress.Street ?? string.Empty,
                request.ShippingAddress.City ?? string.Empty,
                request.ShippingAddress.State ?? string.Empty,
                request.ShippingAddress.Country ?? string.Empty,
                request.ShippingAddress.ZipCode ?? string.Empty);
        }

        var result = await paymentService.PlaceOrderAsync(
            buyerId,
            items,
            shippingAddress,
            _httpContextAccessor.HttpContext?.RequestAborted ?? default);

        return Results.Created($"api/orders/{result.OrderId}", result);
    }
}
