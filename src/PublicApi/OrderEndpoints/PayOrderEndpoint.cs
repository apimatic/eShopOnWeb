using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

public class PayOrderEndpoint : IEndpoint<IResult, PayOrderRequest, IOrderPaymentService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders/{orderId}/pay",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int orderId, PayOrderRequest request, IOrderPaymentService payments, ClaimsPrincipal user) =>
            {
                request.OrderId = orderId;
                request.BuyerId = ApiCaller.BuyerId(user);
                return await HandleAsync(request, payments);
            })
            .Produces<PayOrderResponse>()
            .WithTags("OrderEndpoints");
    }

    public async Task<IResult> HandleAsync(PayOrderRequest request, IOrderPaymentService payments)
    {
        var card = request.Card == null ? null : ApiCaller.ToCard(request.Card);
        var order = await payments.AuthorizeAsync(
            request.OrderId,
            request.BuyerId,
            card,
            request.PaymentMethodId,
            default);

        return Results.Ok(new PayOrderResponse
        {
            OrderId = order.Id,
            Order = ApiCaller.ToDto(order)
        });
    }
}

public class PayOrderRequest : BaseRequest
{
    public int OrderId { get; set; }
    public string BuyerId { get; set; } = string.Empty;
    public CardDetailsRequest? Card { get; set; }
    public int? PaymentMethodId { get; set; }
}

public class PayOrderResponse : BaseResponse
{
    public int OrderId { get; set; }
    public OrderDto Order { get; set; } = new();
}
