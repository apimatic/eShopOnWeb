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

public class ListMyOrdersResponse : BaseResponse
{
    public OrderSummaryDto[] Orders { get; set; } = System.Array.Empty<OrderSummaryDto>();
}

public class ListMyOrdersEndpoint : IEndpoint<IResult, HttpContext, IOrderFlowService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/my-orders",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (HttpContext http, IOrderFlowService orders) =>
            {
                return await HandleAsync(http, orders);
            })
            .Produces<ListMyOrdersResponse>()
            .WithTags("OrderEndpoints");
    }

    public async Task<IResult> HandleAsync(HttpContext http, IOrderFlowService orders)
    {
        var buyerId = http.User.GetBuyerId();
        if (string.IsNullOrEmpty(buyerId))
        {
            return Results.Unauthorized();
        }

        try
        {
            var mine = await orders.GetMyOrdersAsync(buyerId, http.RequestAborted);
            var response = new ListMyOrdersResponse
            {
                Orders = mine.Select(entry => new OrderSummaryDto
                {
                    OrderId = entry.Order.Id,
                    Status = entry.Order.Status.ToString(),
                    OrderDate = entry.Order.OrderDate,
                    Total = entry.Order.Total(),
                    Items = entry.Order.OrderItems.Select(item => new OrderLineDto
                    {
                        CatalogItemId = item.ItemOrdered.CatalogItemId,
                        ProductName = item.ItemOrdered.ProductName,
                        UnitPrice = item.UnitPrice,
                        Quantity = item.Units
                    }).ToList(),
                    Notifications = entry.Notifications.Select(NotificationDto.From).ToList()
                }).ToArray()
            };
            return Results.Ok(response);
        }
        catch (System.Exception ex)
        {
            return EndpointErrors.FromException(ex);
        }
    }
}
