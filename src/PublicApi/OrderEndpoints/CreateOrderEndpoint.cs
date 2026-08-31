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

/// <summary>
/// Places an order for the authenticated shopper from catalog item ids and quantities, reusing the app's
/// existing order/order-item model. The caller's identity comes from the token, not the request body.
/// </summary>
public class CreateOrderEndpoint : IEndpoint<IResult, CreateOrderRequest>
{
    private readonly IOrderService _orderService;
    private readonly IHttpContextAccessor _httpContextAccessor;

    public CreateOrderEndpoint(IOrderService orderService, IHttpContextAccessor httpContextAccessor)
    {
        _orderService = orderService;
        _httpContextAccessor = httpContextAccessor;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (CreateOrderRequest request) =>
            {
                return await HandleAsync(request);
            })
            .Produces<CreateOrderResponse>(StatusCodes.Status201Created)
            .WithTags("OrderEndpoints");
    }

    public async Task<IResult> HandleAsync(CreateOrderRequest request)
    {
        var buyerId = _httpContextAccessor.HttpContext?.User?.Identity?.Name;
        if (string.IsNullOrEmpty(buyerId))
            return Results.Unauthorized();

        if (request.Items is null || request.Items.Count == 0)
            return Results.BadRequest("At least one order item is required.");

        var response = new CreateOrderResponse(request.CorrelationId());

        var items = request.Items.Select(i => new CatalogItemQuantity(i.CatalogItemId, i.Quantity));
        var address = BuildAddress(request.ShipToAddress);

        var order = await _orderService.CreateOrderAsync(buyerId, items, address);

        response.OrderId = order.Id;
        response.Total = order.Total();
        return Results.Created($"api/orders/{order.Id}", response);
    }

    private static Address BuildAddress(ShippingAddressRequest? address)
    {
        if (address is null)
            return new Address("N/A", "N/A", "N/A", "N/A", "00000");

        return new Address(
            string.IsNullOrWhiteSpace(address.Street) ? "N/A" : address.Street,
            string.IsNullOrWhiteSpace(address.City) ? "N/A" : address.City,
            string.IsNullOrWhiteSpace(address.State) ? "N/A" : address.State,
            string.IsNullOrWhiteSpace(address.Country) ? "N/A" : address.Country,
            string.IsNullOrWhiteSpace(address.ZipCode) ? "00000" : address.ZipCode);
    }
}
