using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

public class FulfilOrderRequest : BaseRequest
{
    public int OrderId { get; set; }
}

public class FulfilOrderEndpoint : IEndpoint<IResult, FulfilOrderRequest, IShopOrderService>
{
    private readonly IHttpContextAccessor _httpContextAccessor;

    public FulfilOrderEndpoint(IHttpContextAccessor httpContextAccessor)
    {
        _httpContextAccessor = httpContextAccessor;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders/{orderId}/fulfil",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS, AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int orderId, IShopOrderService orders) =>
            {
                return await HandleAsync(new FulfilOrderRequest { OrderId = orderId }, orders);
            })
            .Produces<ShopOrderResponse>()
            .WithTags("OrderEndpoints");
    }

    public async Task<IResult> HandleAsync(FulfilOrderRequest request, IShopOrderService orders)
    {
        var result = await orders.FulfilAsync(
            request.OrderId,
            _httpContextAccessor.HttpContext?.RequestAborted ?? default);
        return Results.Ok(ShopOrderResponse.From(result, request.CorrelationId()));
    }
}
