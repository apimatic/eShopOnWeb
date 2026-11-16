using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.PaymentEndpoints;

/// <summary>Authorizes (holds) the order total using a one-off card or a saved card.</summary>
public class PayOrderEndpoint : IEndpoint<IResult, PayOrderRequest, IOrderPaymentService, ClaimsPrincipal>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders/{orderId}/pay",
                [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
                (int orderId, PayOrderRequest request, IOrderPaymentService paymentService, ClaimsPrincipal user) =>
                {
                    request.OrderId = orderId;
                    return await HandleAsync(request, paymentService, user);
                })
            .Produces<PayOrderResponse>()
            .WithTags("OrderEndpoints");
    }

    public async Task<IResult> HandleAsync(PayOrderRequest request, IOrderPaymentService paymentService,
        ClaimsPrincipal user)
    {
        var buyerId = user.Identity?.Name;
        var payment = await paymentService.AuthorizeAsync(buyerId!, request.OrderId, request);
        var response = new PayOrderResponse(request.CorrelationId())
        {
            OrderId = request.OrderId,
            Payment = payment
        };

        return Results.Ok(response);
    }
}
