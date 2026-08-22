using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.PublicApi.Notifications;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

public class ListMyOrdersEndpoint : IEndpoint<IResult, IOrderWorkflowService>
{
    private readonly IOrderNotificationService _notificationService;

    public ListMyOrdersEndpoint(IOrderNotificationService notificationService)
    {
        _notificationService = notificationService;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/my-orders",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
            async (HttpContext httpContext, IOrderWorkflowService service) =>
            {
                return await HandleAsync(httpContext, service);
            })
            .Produces<ListMyOrdersResponse>()
            .WithTags("OrderEndpoints");
    }

    public Task<IResult> HandleAsync(IOrderWorkflowService request)
        => HandleAsync(null!, request);

    private async Task<IResult> HandleAsync(HttpContext httpContext, IOrderWorkflowService service)
    {
        var buyerId = httpContext.User.Identity?.Name ?? httpContext.User.FindFirstValue(ClaimTypes.Name);
        if (buyerId == null)
        {
            return Results.Unauthorized();
        }

        var orders = await service.ListBuyerOrdersAsync(buyerId);
        var response = new ListMyOrdersResponse();
        foreach (var order in orders)
        {
            var notifications = await _notificationService.ListForBuyerOrderAsync(buyerId, order.Id);
            response.Orders.Add(new OrderSummaryDto
            {
                OrderId = order.Id,
                Status = order.Status.ToString(),
                OrderDate = order.OrderDate,
                Total = order.Total(),
                Items = order.OrderItems.Select(i => new OrderItemDto
                {
                    CatalogItemId = i.ItemOrdered.CatalogItemId,
                    ProductName = i.ItemOrdered.ProductName,
                    UnitPrice = i.UnitPrice,
                    Units = i.Units
                }).ToList(),
                Notifications = notifications.Select(OrderNotificationDto.FromEntity).ToList()
            });
        }

        return Results.Ok(response);
    }
}
