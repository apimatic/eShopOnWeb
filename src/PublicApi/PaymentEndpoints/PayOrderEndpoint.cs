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

namespace Microsoft.eShopWeb.PublicApi.PaymentEndpoints;

/// <summary>
/// Authorizes (holds) the order total using a one-off card or one of the caller's saved cards. The
/// money is held, not taken. Shopper-scoped: acts only on the caller's own order.
/// </summary>
public class PayOrderEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders/{orderId}/pay",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int orderId, PayOrderRequestDto request, IPaymentService paymentService, ClaimsPrincipal user, CancellationToken ct) =>
            {
                var buyerId = PaymentMapping.GetBuyerId(user);

                var card = request.Card is null ? null : PaymentMapping.ToCardData(request.Card);
                var order = await paymentService.AuthorizeOrderAsync(orderId, buyerId, card, request.SavedPaymentMethodId, ct);

                var response = new PayOrderResponseDto
                {
                    OrderId = order.Id,
                    Status = order.Status.ToString(),
                    Payment = PaymentViewMapping.ToPaymentState(order.Payment)
                };
                return Results.Ok(response);
            })
            .Produces<PayOrderResponseDto>()
            .WithTags("PaymentEndpoints");
    }
}
