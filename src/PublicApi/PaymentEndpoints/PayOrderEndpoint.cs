using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.PaymentEndpoints;

/// <summary>
/// POST /api/orders/{orderId}/pay — authorize (hold) the order total with a card or a saved card.
/// Does not capture. Idempotent in effect: a double-click never places a second hold.
/// </summary>
public class PayOrderEndpoint : IEndpoint<IResult, PayOrderRequest, IPaymentService, ClaimsPrincipal>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders/{orderId}/pay",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int orderId, PayOrderRequest request, IPaymentService paymentService, ClaimsPrincipal user) =>
            {
                request ??= new PayOrderRequest();
                request.OrderId = orderId;
                return await HandleAsync(request, paymentService, user);
            })
            .WithTags("OrderPaymentEndpoints");
    }

    public async Task<IResult> HandleAsync(PayOrderRequest request, IPaymentService paymentService, ClaimsPrincipal user)
    {
        var buyerId = PaymentApi.BuyerId(user);
        if (string.IsNullOrEmpty(buyerId))
            return Results.Unauthorized();

        var instruction = new PayInstruction
        {
            Card = request.Card?.ToGateway(),
            SavedPaymentMethodId = request.SavedPaymentMethodId
        };

        var result = await paymentService.PayOrderAsync(buyerId, request.OrderId, instruction);
        return PaymentApi.ToHttp(result, PaymentApi.PaymentDto);
    }
}
