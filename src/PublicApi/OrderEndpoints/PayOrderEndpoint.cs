using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

public class PayOrderRequest : BaseRequest
{
    public int OrderId { get; set; }
    public CardDetailsRequest? Card { get; set; }
    public int? PaymentMethodId { get; set; }
}

public class PayOrderResponse : BaseResponse
{
    public int OrderId { get; set; }
    public OrderDto Order { get; set; } = new();
}

public class PayOrderEndpoint : IEndpoint<IResult, PayOrderRequest, IOrderPaymentService>
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
            (int orderId, PayOrderRequest request, IOrderPaymentService orders) =>
            {
                request.OrderId = orderId;
                return await HandleAsync(request, orders);
            })
            .Produces<PayOrderResponse>()
            .WithTags("OrderEndpoints");
    }

    public async Task<IResult> HandleAsync(PayOrderRequest request, IOrderPaymentService orders)
    {
        var buyerId = CreateOrderEndpoint.RequireBuyerId(_httpContextAccessor.HttpContext?.User);
        var card = request.Card is null ? null : PaymentApiMapper.ToCardSource(request.Card);
        var order = await orders.AuthorizePaymentAsync(buyerId, request.OrderId, card, request.PaymentMethodId);
        return Results.Ok(new PayOrderResponse
        {
            OrderId = order.Id,
            Order = PaymentApiMapper.ToDto(order)
        });
    }
}
