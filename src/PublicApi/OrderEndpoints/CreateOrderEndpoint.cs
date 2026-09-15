using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.PublicApi.PaymentEndpoints;
using MinimalApi.Endpoint;
using Swashbuckle.AspNetCore.Annotations;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

/// <summary>
/// Places an order from catalog items for the signed-in shopper, reusing the app's existing
/// Order/OrderItem model. The order starts awaiting payment; no money is held yet.
/// </summary>
public class CreateOrderEndpoint : IEndpoint<IResult, CreateOrderRequest, IOrderPaymentService>
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
            async (CreateOrderRequest request, IOrderPaymentService service) => await HandleAsync(request, service))
            .Produces<OrderPaymentResponse>(StatusCodes.Status201Created)
            .WithTags("OrderEndpoints");
    }

    public async Task<IResult> HandleAsync(CreateOrderRequest request, IOrderPaymentService service)
    {
        var buyerId = _httpContextAccessor.HttpContext!.User.GetBuyerId();

        var lines = (request.Items ?? new()).Select(i => new OrderLine(i.CatalogItemId, i.Quantity));
        var address = BuildAddress(request.ShipToAddress);

        var order = await service.PlaceOrderAsync(buyerId, lines, address);
        var response = PaymentApiMapper.ToResponse(order);
        return Results.Created($"api/orders/{order.Id}", response);
    }

    private static Address BuildAddress(ShipToAddressDto? dto) => new(
        string.IsNullOrWhiteSpace(dto?.Street) ? "N/A" : dto!.Street!,
        string.IsNullOrWhiteSpace(dto?.City) ? "N/A" : dto!.City!,
        string.IsNullOrWhiteSpace(dto?.State) ? "N/A" : dto!.State!,
        string.IsNullOrWhiteSpace(dto?.Country) ? "US" : dto!.Country!,
        string.IsNullOrWhiteSpace(dto?.ZipCode) ? "00000" : dto!.ZipCode!);
}
