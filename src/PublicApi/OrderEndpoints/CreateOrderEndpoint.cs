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
/// Places an order from catalog items for the signed-in shopper and notifies them by SMS.
/// </summary>
public class CreateOrderEndpoint : IEndpoint<IResult, CreateOrderRequest, ICatalogOrderService>
{
    private readonly IHttpContextAccessor _httpContextAccessor;
    private readonly IOrderNotificationService _notificationService;

    public CreateOrderEndpoint(
        IHttpContextAccessor httpContextAccessor,
        IOrderNotificationService notificationService)
    {
        _httpContextAccessor = httpContextAccessor;
        _notificationService = notificationService;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
            async (CreateOrderRequest request, ICatalogOrderService orderService) =>
            {
                return await HandleAsync(request, orderService);
            })
            .Produces<CreateOrderResponse>(StatusCodes.Status201Created)
            .Produces(StatusCodes.Status400BadRequest)
            .WithTags("OrderEndpoints");
    }

    public async Task<IResult> HandleAsync(CreateOrderRequest request, ICatalogOrderService orderService)
    {
        var buyerId = _httpContextAccessor.HttpContext?.GetBuyerId();
        if (string.IsNullOrEmpty(buyerId))
        {
            return Results.Unauthorized();
        }

        Address? shipTo = null;
        if (request.ShipTo is not null)
        {
            shipTo = new Address(
                request.ShipTo.Street,
                request.ShipTo.City,
                request.ShipTo.State,
                request.ShipTo.Country,
                request.ShipTo.ZipCode);
        }

        var lines = request.Items
            .Select(i => new CatalogOrderLine(i.CatalogItemId, i.Quantity))
            .ToList();

        var order = await orderService.PlaceOrderAsync(buyerId, lines, shipTo);
        await _notificationService.NotifyOrderPlacedAsync(order);

        var response = new CreateOrderResponse(request.CorrelationId())
        {
            OrderId = order.Id,
            Status = order.Status.ToString(),
            Total = order.Total()
        };
        return Results.Created($"api/orders/{order.Id}", response);
    }
}
