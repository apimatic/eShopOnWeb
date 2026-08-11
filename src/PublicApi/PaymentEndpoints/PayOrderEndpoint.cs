using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Payments;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.PaymentEndpoints;

/// <summary>
/// POST /api/orders/{orderId}/pay — authorizes the order total (holds the money, does not take it)
/// using either one-off card details or one of the shopper's saved cards. Idempotent in effect.
/// </summary>
public class PayOrderEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders/{orderId:int}/pay",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async (
                int orderId,
                PayOrderRequest request,
                ClaimsPrincipal user,
                IOrderPaymentService orderPaymentService,
                CancellationToken cancellationToken) =>
            await PaymentProblem.ExecuteAsync(async () =>
            {
                var buyerId = user.GetBuyerId();
                var instruction = new PaymentInstruction(request.Card?.ToCardDetails(), request.SavedCardId);

                var payment = await orderPaymentService.PayOrderAsync(buyerId, orderId, instruction, cancellationToken);

                return Results.Ok(PaymentDto.From(payment));
            }))
            .Produces<PaymentDto>()
            .WithTags("OrderEndpoints");
    }
}
