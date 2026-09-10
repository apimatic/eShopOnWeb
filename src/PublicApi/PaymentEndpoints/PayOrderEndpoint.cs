using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.PayPal;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.PaymentEndpoints;

/// <summary>
/// POST /api/orders/{orderId}/pay — authorizes (holds) the order total on a card. The money is not taken.
/// Pays with a one-off card or one of the shopper's saved cards. Shopper-scoped.
/// </summary>
public class PayOrderEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders/{orderId:int}/pay",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async (
                int orderId,
                PayOrderRequest request,
                IPaymentService paymentService,
                ClaimsPrincipal user,
                HttpContext http) =>
            {
                var buyerId = user.GetBuyerId();
                if (string.IsNullOrEmpty(buyerId))
                    return Results.Unauthorized();

                if ((request.Card is null) == (request.SavedPaymentMethodId is null))
                    return Results.BadRequest(new { message = "Supply exactly one of 'card' or 'savedPaymentMethodId'." });

                CardDetails? card = request.Card?.ToCardDetails();
                var view = await paymentService.AuthorizeAsync(
                    orderId, buyerId, card, request.SavedPaymentMethodId, http.RequestAborted);

                return Results.Ok(view);
            })
            .Produces<OrderPaymentView>()
            .WithTags("PaymentEndpoints");
    }
}
