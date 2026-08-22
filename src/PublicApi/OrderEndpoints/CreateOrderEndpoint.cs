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

public class CreateOrderEndpoint : IEndpoint<IResult, CreateOrderRequest, IOrderService>
{
    private readonly IHttpContextAccessor _httpContextAccessor;
    private readonly IOrderNotificationService _notificationService;

    public CreateOrderEndpoint(IHttpContextAccessor httpContextAccessor, IOrderNotificationService notificationService)
    {
        _httpContextAccessor = httpContextAccessor;
        _notificationService = notificationService;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
            async (CreateOrderRequest request, IOrderService orderService) =>
            {
                return await HandleAsync(request, orderService);
            })
            .Produces<CreateOrderResponse>(StatusCodes.Status201Created)
            .Produces(StatusCodes.Status400BadRequest)
            .WithTags("OrderEndpoints");
    }

    public async Task<IResult> HandleAsync(CreateOrderRequest request, IOrderService orderService)
    {
        var buyerId = _httpContextAccessor.HttpContext!.GetBuyerId();
        var lines = request.Items.Select(i => new CatalogOrderLine(i.CatalogItemId, i.Quantity)).ToList();
        var order = await orderService.CreateCatalogOrderAsync(buyerId, lines);
        await _notificationService.NotifyOrderPlacedAsync(order);

        var notifications = await _notificationService.ListForOrderAsync(order.Id, refreshFromProvider: false);
        var response = new CreateOrderResponse(request.CorrelationId())
        {
            OrderId = order.Id,
            Status = order.Status.ToString(),
            Total = order.Total()
        };
        response.NotificationIds.AddRange(notifications.Select(n => n.Id));
        return Results.Created($"api/orders/{order.Id}", response);
    }
}
