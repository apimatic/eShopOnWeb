using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

/// <summary>
/// Authorizes (holds) the order total at PayPal. Pays either with one-off card
/// details or with one of the caller's saved cards. Idempotent: paying an
/// already-authorized order returns the existing authorization.
/// </summary>
public class PayOrderEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders/{orderId}/pay",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int orderId, PayOrderRequest request, ClaimsPrincipal user, IPaymentService paymentService, CancellationToken ct) =>
            {
                var buyerId = user.Identity?.Name;
                if (string.IsNullOrEmpty(buyerId))
                {
                    return Results.Unauthorized();
                }

                var card = request.Card is null ? null : CardDetailsMapper.ToCardDetails(request.Card);
                var payment = await paymentService.PayOrderAsync(buyerId, orderId, card, request.PaymentMethodId, ct);

                var response = new PayOrderResponse(request.CorrelationId())
                {
                    OrderId = orderId,
                    OrderStatus = "PaymentAuthorized",
                    Payment = PaymentDto.FromPayment(payment)
                };
                return Results.Ok(response);
            })
            .Produces<PayOrderResponse>()
            .WithTags("OrderEndpoints");
    }
}
