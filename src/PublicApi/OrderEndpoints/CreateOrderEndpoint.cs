using System.Collections.Generic;
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

public record CreateOrderItemRequest(int CatalogItemId, int Quantity);

public record CreateOrderRequest(List<CreateOrderItemRequest> Items);

public record CreateOrderResponse(int OrderId, string Status, decimal Total, string Currency);

/// <summary>
/// POST /api/orders — places an order from catalog items for the signed-in shopper. Prices come from
/// the catalog; the order starts awaiting payment. Returns the new order id as a top-level field.
/// </summary>
public class CreateOrderEndpoint : IEndpoint<IResult, CreateOrderRequest, IOrderPlacementService>
{
    private readonly IHttpContextAccessor _httpContextAccessor;
    private readonly IPaymentSettings _settings;

    public CreateOrderEndpoint(IHttpContextAccessor httpContextAccessor, IPaymentSettings settings)
    {
        _httpContextAccessor = httpContextAccessor;
        _settings = settings;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (CreateOrderRequest request, IOrderPlacementService orderPlacementService) =>
                await HandleAsync(request, orderPlacementService))
            .Produces<CreateOrderResponse>(StatusCodes.Status201Created)
            .WithTags("OrderEndpoints");
    }

    public async Task<IResult> HandleAsync(CreateOrderRequest request, IOrderPlacementService orderPlacementService)
    {
        var buyerId = _httpContextAccessor.GetBuyerId();

        var lines = (request.Items ?? new List<CreateOrderItemRequest>())
            .Select(i => new OrderLineRequest(i.CatalogItemId, i.Quantity))
            .ToList();

        var order = await orderPlacementService.PlaceOrderAsync(buyerId, lines);

        var response = new CreateOrderResponse(order.Id, order.Status.ToString(), order.Total(), _settings.Currency);
        return Results.Created($"api/orders/{order.Id}", response);
    }
}
