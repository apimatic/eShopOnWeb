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

public class CreateOrderEndpoint : IEndpoint<IResult, CreateOrderRequest, ICatalogCheckoutService>
{
    private readonly IOrderNotificationService _notificationService;

    public CreateOrderEndpoint(IOrderNotificationService notificationService)
    {
        _notificationService = notificationService;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
            async (CreateOrderRequest request, HttpContext httpContext, ICatalogCheckoutService checkoutService) =>
            {
                return await HandleAsync(request, checkoutService, httpContext);
            })
            .Produces<CreateOrderResponse>(StatusCodes.Status201Created)
            .Produces(StatusCodes.Status400BadRequest)
            .WithTags("OrderEndpoints");
    }

    public Task<IResult> HandleAsync(CreateOrderRequest request, ICatalogCheckoutService checkoutService)
    {
        return HandleAsync(request, checkoutService, null!);
    }

    private async Task<IResult> HandleAsync(CreateOrderRequest request, ICatalogCheckoutService checkoutService, HttpContext httpContext)
    {
        var buyerId = httpContext.User.RequireBuyerId();
        var items = request.Items.Select(i => new CatalogOrderItem(i.CatalogItemId, i.Quantity)).ToList();
        var order = await checkoutService.PlaceOrderAsync(buyerId, items);
        var notifications = await _notificationService.ListForOrderAsync(order.Id);

        var response = new CreateOrderResponse
        {
            OrderId = order.Id,
            Status = order.Status.ToString(),
            Total = order.Total(),
            Notifications = notifications.Select(NotificationDto.FromEntity).ToList()
        };

        return Results.Created($"api/orders/{order.Id}", response);
    }
}
