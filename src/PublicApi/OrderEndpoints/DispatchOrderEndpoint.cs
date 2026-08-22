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

public class DispatchOrderRequest : BaseRequest
{
    public int OrderId { get; set; }
}

public class DispatchOrderEndpoint : IEndpoint<IResult, DispatchOrderRequest, IShopperOrderService>
{
    private readonly IHttpContextAccessor _httpContextAccessor;

    public DispatchOrderEndpoint(IHttpContextAccessor httpContextAccessor)
    {
        _httpContextAccessor = httpContextAccessor;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders/{orderId}/dispatch",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS, AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int orderId, IShopperOrderService orderService) =>
            {
                return await HandleAsync(new DispatchOrderRequest { OrderId = orderId }, orderService);
            })
            .Produces<OrderActionResponse>()
            .WithTags("OrderEndpoints");
    }

    public async Task<IResult> HandleAsync(DispatchOrderRequest request, IShopperOrderService orderService)
    {
        try
        {
            var order = await orderService.DispatchAsync(request.OrderId, _httpContextAccessor.HttpContext?.RequestAborted ?? default);
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
