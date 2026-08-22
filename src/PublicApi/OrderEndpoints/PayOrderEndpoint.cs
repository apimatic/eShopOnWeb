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
            (int orderId, PayOrderRequest request, IOrderPaymentService paymentService, HttpContext httpContext) =>
            {
                request.OrderId = orderId;
                return await HandleAsync(request, paymentService, httpContext);
            })
            .Produces<OrderResponse>()
            .WithTags("OrderEndpoints");
    }

    public Task<IResult> HandleAsync(PayOrderRequest request, IOrderPaymentService paymentService) =>
        HandleAsync(request, paymentService, null!);

    private async Task<IResult> HandleAsync(
        PayOrderRequest request,
        IOrderPaymentService paymentService,
        HttpContext httpContext)
    {
        var buyerId = httpContext.GetRequiredUserName();
        var card = request.Card == null ? null : PaymentMapping.ToCardSource(request.Card);
        var order = await paymentService.PayAsync(request.OrderId, buyerId, card, request.PaymentMethodId);
        return Results.Ok(PaymentMapping.ToOrderResponse(order));
    }
}

public class PayOrderRequest
{
    public int OrderId { get; set; }
    public int? PaymentMethodId { get; set; }
    public CardDetailsRequest? Card { get; set; }
}
