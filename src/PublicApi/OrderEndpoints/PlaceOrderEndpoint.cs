using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Specifications;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

public class PlaceOrderEndpoint : IEndpoint<IResult, PlaceOrderRequest, IOrderWorkflowService>
{
    private readonly IHttpContextAccessor _httpContextAccessor;
    private readonly IRepository<Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate.OrderNotification> _notifications;

    public PlaceOrderEndpoint(
        IHttpContextAccessor httpContextAccessor,
        IRepository<Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate.OrderNotification> notifications)
    {
        _httpContextAccessor = httpContextAccessor;
        _notifications = notifications;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (PlaceOrderRequest request, IOrderWorkflowService orders) =>
            {
                return await HandleAsync(request, orders);
            })
            .Produces<PlaceOrderResponse>(StatusCodes.Status201Created)
            .Produces(StatusCodes.Status400BadRequest)
            .WithTags("OrderEndpoints");
    }

    public async Task<IResult> HandleAsync(PlaceOrderRequest request, IOrderWorkflowService orders)
    {
        var buyerId = HttpContextBuyer.GetBuyerId(_httpContextAccessor.HttpContext!);
        if (string.IsNullOrEmpty(buyerId))
        {
            return Results.Unauthorized();
        }

        Address? address = null;
        if (request.ShipToAddress != null)
        {
            address = new Address(
                request.ShipToAddress.Street,
                request.ShipToAddress.City,
                request.ShipToAddress.State,
                request.ShipToAddress.Country,
                request.ShipToAddress.ZipCode);
        }

        var items = request.Items
            .Select(i => new PlaceOrderItem(i.CatalogItemId, i.Quantity))
            .ToList();

        var order = await orders.PlaceOrderAsync(buyerId, items, address);
        var notifications = await _notifications.ListAsync(new OrderNotificationsByOrderIdSpecification(order.Id));

        var response = new PlaceOrderResponse(request.CorrelationId())
        {
            OrderId = order.Id,
            Status = order.Status.ToString(),
            Total = order.Total()
        };
        response.Notifications.AddRange(notifications.Select(NotificationDto.From));

        return Results.Created($"api/orders/{order.Id}", response);
    }
}
