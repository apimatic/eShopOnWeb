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
            async (int orderId, PayOrderRequest? request, HttpContext httpContext, IOrderPaymentService paymentService) =>
            {
                request ??= new PayOrderRequest();
                request.OrderId = orderId;
                request.BuyerId = PaymentHttp.BuyerId(httpContext);
                return await HandleAsync(request, paymentService);
            })
            .Produces<OrderResponse>()
            .Produces(StatusCodes.Status400BadRequest)
            .WithTags("OrderEndpoints");
    }

    public async Task<IResult> HandleAsync(PayOrderRequest request, IOrderPaymentService paymentService)
    {
        try
        {
            var order = await paymentService.PayAsync(
                request.BuyerId,
                request.OrderId,
                request.Card?.ToCardPaymentSource(),
                request.PaymentMethodId);
            return Results.Ok(OrderResponse.From(order));
        }
        catch (System.Exception ex)
        {
            return PaymentHttp.FromException(ex);
        }
    }
}

public class PayOrderRequest
{
    public string BuyerId { get; set; } = string.Empty;
    public int OrderId { get; set; }
    public int? PaymentMethodId { get; set; }
    public CardRequest? Card { get; set; }
}
