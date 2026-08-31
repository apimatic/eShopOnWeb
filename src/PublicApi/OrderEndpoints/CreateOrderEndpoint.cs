using System.Linq;
using System.Security.Claims;
using System.Threading;
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
/// Places an order from catalog items and quantities for the authenticated shopper. Reuses the app's
/// existing order/order-item model; the caller's identity comes from the token.
/// </summary>
public class CreateOrderEndpoint : IEndpoint<IResult, CreateOrderRequest, ClaimsPrincipal>
{
    private readonly IOrderPlacementService _orderPlacementService;

    public CreateOrderEndpoint(IOrderPlacementService orderPlacementService)
    {
        _orderPlacementService = orderPlacementService;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (CreateOrderRequest request, ClaimsPrincipal user, CancellationToken ct) =>
            {
                return await HandleAsync(request, user, ct);
            })
            .Produces<CreateOrderResponse>(StatusCodes.Status201Created)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .WithTags("OrderEndpoints");
    }

    public Task<IResult> HandleAsync(CreateOrderRequest request, ClaimsPrincipal user) => HandleAsync(request, user, default);

    public async Task<IResult> HandleAsync(CreateOrderRequest request, ClaimsPrincipal user, CancellationToken ct)
    {
        var buyerId = user.FindFirstValue(ClaimTypes.Name);
        if (string.IsNullOrEmpty(buyerId))
        {
            return Results.Unauthorized();
        }

        var response = new CreateOrderResponse(request.CorrelationId());

        var lines = request.Items.Select(i => new OrderLineRequest(i.CatalogItemId, i.Quantity));
        var shipToAddress = BuildAddress(request.ShipToAddress);

        var order = await _orderPlacementService.PlaceOrderAsync(buyerId, lines, shipToAddress, ct);

        response.OrderId = order.Id;
        response.Total = order.Total();

        return Results.Created($"api/orders/{order.Id}", response);
    }

    private static Address BuildAddress(ShippingAddressDto? dto)
    {
        // Shipping is not part of the invoicing flow; a placeholder keeps the existing Order model valid.
        return new Address(
            Coalesce(dto?.Street, "N/A"),
            Coalesce(dto?.City, "N/A"),
            Coalesce(dto?.State, "N/A"),
            Coalesce(dto?.Country, "N/A"),
            Coalesce(dto?.ZipCode, "00000"));
    }

    private static string Coalesce(string? value, string fallback) =>
        string.IsNullOrWhiteSpace(value) ? fallback : value;
}
