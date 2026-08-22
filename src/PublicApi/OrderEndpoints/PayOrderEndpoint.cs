using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

public class PayOrderRequest : BaseRequest
{
    public int OrderId { get; set; }
    public int? PaymentMethodId { get; set; }
    public CardDetailsDto? Card { get; set; }
}

public class PayOrderEndpoint : IEndpoint<IResult, PayOrderRequest, IShopOrderService>
{
    private readonly IHttpContextAccessor _httpContextAccessor;

    public PayOrderEndpoint(IHttpContextAccessor httpContextAccessor)
    {
        _httpContextAccessor = httpContextAccessor;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders/{orderId}/pay",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int orderId, PayOrderRequest request, IShopOrderService orders) =>
            {
                request.OrderId = orderId;
                return await HandleAsync(request, orders);
            })
            .Produces<ShopOrderResponse>()
            .WithTags("OrderEndpoints");
    }

    public async Task<IResult> HandleAsync(PayOrderRequest request, IShopOrderService orders)
    {
        var buyerId = BuyerIdentity.Require(_httpContextAccessor);
        var card = request.Card is null ? null : CardDetailsMapping.ToSource(request.Card);
        var result = await orders.PayAsync(
            buyerId,
            request.OrderId,
            card,
            request.PaymentMethodId,
            _httpContextAccessor.HttpContext?.RequestAborted ?? default);
        return Results.Ok(ShopOrderResponse.From(result, request.CorrelationId()));
    }
}
