using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Payments;
using Microsoft.eShopWeb.PublicApi.PaymentModels;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.OrderPaymentEndpoints;

/// <summary>
/// POST /api/orders — place an order from catalog items for the signed-in shopper. The order
/// starts awaiting payment. Returns the created order's identifier as a top-level field.
/// </summary>
public class CreateOrderEndpoint : IEndpoint<IResult, PlaceOrderRequest, IOrderPaymentService>
{
    // Placeholder used when the caller does not supply a shipping address (this API is about
    // payment, not fulfilment logistics); the order model requires a non-empty address.
    private static readonly Address DefaultShipTo = new("N/A", "N/A", "N/A", "N/A", "00000");

    private readonly IHttpContextAccessor _httpContextAccessor;

    public CreateOrderEndpoint(IHttpContextAccessor httpContextAccessor)
        => _httpContextAccessor = httpContextAccessor;

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (PlaceOrderRequest request, IOrderPaymentService service) =>
                await HandleAsync(request, service))
            .Produces<CreateOrderResponse>(StatusCodes.Status201Created)
            .WithTags("OrderPaymentEndpoints");
    }

    public async Task<IResult> HandleAsync(PlaceOrderRequest request, IOrderPaymentService service)
    {
        var buyerId = _httpContextAccessor.HttpContext!.User.GetBuyerId();
        var lines = request.Items.Select(i => new OrderLine(i.CatalogItemId, i.Quantity)).ToList();
        var shipTo = request.ShipToAddress?.ToDomain() ?? DefaultShipTo;

        var order = await service.PlaceOrderAsync(buyerId, lines, shipTo);

        var response = new CreateOrderResponse { OrderId = order.Id, Order = OrderView.From(order) };
        return Results.Created($"api/orders/{order.Id}", response);
    }
}

public class CreateOrderResponse
{
    public int OrderId { get; set; }
    public OrderView Order { get; set; } = new();
}
