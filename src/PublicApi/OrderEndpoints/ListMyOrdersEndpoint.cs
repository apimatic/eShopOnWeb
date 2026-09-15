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

public class MyOrderDto
{
    public int OrderId { get; set; }
    public string Status { get; set; } = string.Empty;
    public decimal Total { get; set; }
    public List<NotificationDto> Notifications { get; set; } = new();
}

public class ListMyOrdersResponse : BaseResponse
{
    public List<MyOrderDto> Orders { get; set; } = new();
}

public class ListMyOrdersEndpoint : IEndpoint<IResult, IShopperOrderService>
{
    private readonly IHttpContextAccessor _httpContextAccessor;

    public ListMyOrdersEndpoint(IHttpContextAccessor httpContextAccessor)
    {
        _httpContextAccessor = httpContextAccessor;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/my-orders",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (IShopperOrderService service) =>
            {
                return await HandleAsync(service);
            })
            .Produces<ListMyOrdersResponse>()
            .WithTags("OrderEndpoints");
    }

    public async Task<IResult> HandleAsync(IShopperOrderService service)
    {
        var httpContext = _httpContextAccessor.HttpContext!;
        var orders = await service.ListMyOrdersAsync(
            BuyerIdentity.RequireBuyerId(httpContext),
            httpContext.RequestAborted);

        var response = new ListMyOrdersResponse();
        response.Orders.AddRange(orders.Select(o => new MyOrderDto
        {
            OrderId = o.Order.Id,
            Status = NotificationDtoMapper.OrderStatusName(o.Order.Status),
            Total = o.Order.Total(),
            Notifications = o.Notifications.Select(NotificationDtoMapper.ToDto).ToList()
        }));

        return Results.Ok(response);
    }
}
