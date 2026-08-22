using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

public class CancelOrderRequest : BaseRequest
{
    public int OrderId { get; set; }
}

public class CancelOrderEndpoint : IEndpoint<IResult, CancelOrderRequest, IShopOrderService>
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
            (int orderId, IShopOrderService orders) =>
            {
                return await HandleAsync(new CancelOrderRequest { OrderId = orderId }, orders);
            })
            .Produces<ShopOrderResponse>()
            .WithTags("OrderEndpoints");
    }

    public async Task<IResult> HandleAsync(CancelOrderRequest request, IShopOrderService orders)
    {
        var result = await orders.CancelAsync(
            request.OrderId,
            _httpContextAccessor.HttpContext?.RequestAborted ?? default);
        return Results.Ok(ShopOrderResponse.From(result, request.CorrelationId()));
    }
}
