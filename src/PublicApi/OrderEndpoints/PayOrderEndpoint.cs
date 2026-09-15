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
        app.MapPost("api/orders/{orderId:int}/pay",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
            async (int orderId, PayOrderRequest request, IOrderPaymentService service, HttpContext httpContext) =>
            {
                request.OrderId = orderId;
                request.BuyerId = httpContext.RequireBuyerId();
                return await HandleAsync(request, service);
            })
            .Produces<OrderDto>()
            .WithTags("OrderEndpoints");
    }

    public async Task<IResult> HandleAsync(PayOrderRequest request, IOrderPaymentService service)
    {
        var order = await service.PayAsync(
            request.OrderId,
            request.BuyerId,
            request.Card?.ToInput(),
            request.PaymentMethodId);
        return Results.Ok(OrderDtoMapper.ToDto(order));
    }
}

public class PayOrderRequest : BaseRequest
{
    public int OrderId { get; set; }
    public int? PaymentMethodId { get; set; }
    public CardPaymentRequest? Card { get; set; }
    [System.Text.Json.Serialization.JsonIgnore]
    public string BuyerId { get; set; } = string.Empty;
}
