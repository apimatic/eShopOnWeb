using System;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

public class CancelOrderRequest : BaseRequest
{
    public int OrderId { get; set; }
}

public class CancelOrderEndpoint : IEndpoint<IResult, CancelOrderRequest, IShopperOrderService>
{
    private readonly IHttpContextAccessor _httpContextAccessor;

    public CancelOrderEndpoint(IHttpContextAccessor httpContextAccessor)
    {
        _httpContextAccessor = httpContextAccessor;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders/{orderId}/cancel",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS, AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int orderId, IShopperOrderService orderService) =>
            {
                return await HandleAsync(new CancelOrderRequest { OrderId = orderId }, orderService);
            })
            .Produces<OrderActionResponse>()
            .WithTags("OrderEndpoints");
    }

    public async Task<IResult> HandleAsync(CancelOrderRequest request, IShopperOrderService orderService)
    {
        try
        {
            var order = await orderService.CancelAsync(request.OrderId, _httpContextAccessor.HttpContext?.RequestAborted ?? default);
            return Results.Ok(new OrderActionResponse
            {
                OrderId = order.Id,
                FulfillmentStatus = order.FulfillmentStatus
            });
        }
        catch (Exception ex)
        {
            return ex.ToHttpResult();
        }
    }
}
