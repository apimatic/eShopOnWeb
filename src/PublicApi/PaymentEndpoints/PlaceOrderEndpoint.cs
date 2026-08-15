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

namespace Microsoft.eShopWeb.PublicApi.PaymentEndpoints;

/// <summary>
/// Places an order for the signed-in shopper from catalog item ids and quantities. The order starts
/// awaiting payment. Returns the created order's id as a top-level field.
/// </summary>
public class PlaceOrderEndpoint : IEndpoint<IResult, PlaceOrderRequest, IOrderService>
{
    private readonly IHttpContextAccessor _http;

    public PlaceOrderEndpoint(IHttpContextAccessor http) => _http = http;

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (PlaceOrderRequest request, IOrderService orderService) =>
                await HandleAsync(request, orderService))
            .Produces<PlaceOrderResponse>(StatusCodes.Status201Created)
            .WithTags("PaymentEndpoints");
    }

    public async Task<IResult> HandleAsync(PlaceOrderRequest request, IOrderService orderService)
    {
        var buyerId = EndpointCaller.RequireBuyerId(_http);

        if (request.Items is null || !request.Items.Any())
        {
            return Results.BadRequest("At least one order item is required.");
        }

        var address = request.ShipToAddress is null
            ? new Address("123 Main St.", "Kent", "OH", "United States", "44240")
            : new Address(
                request.ShipToAddress.Street,
                request.ShipToAddress.City,
                request.ShipToAddress.State,
                request.ShipToAddress.Country,
                request.ShipToAddress.ZipCode);

        var lines = request.Items.Select(i => new OrderLine(i.CatalogItemId, i.Quantity));
        var order = await orderService.CreateOrderAsync(buyerId, lines, address);

        var response = new PlaceOrderResponse
        {
            OrderId = order.Id,
            Status = order.Status.ToString(),
            Total = order.Total(),
            Items = PaymentMapping.ToLineDtos(order)
        };

        return Results.Created($"api/orders/{order.Id}", response);
    }
}
