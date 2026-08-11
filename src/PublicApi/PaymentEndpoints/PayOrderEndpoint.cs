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
/// Authorizes (holds) the order total. Pays with one-off card details, or with one of the shopper's
/// saved cards. The hold equals the order total to the cent; the money is not taken until fulfilment.
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
                IPaymentService paymentService,
                CancellationToken ct) =>
            {
                var buyerId = user.BuyerId();
                if (string.IsNullOrEmpty(buyerId))
                {
                    return Results.Unauthorized();
                }

                var card = request.Card?.ToCardDetails();
                var payment = await paymentService.AuthorizeAsync(orderId, buyerId, request.SavedCardId, card, ct);

                var response = new OrderPaymentResponse
                {
                    OrderId = orderId,
                    Status = PaymentMapping.OrderStatus(payment),
                    Payment = PaymentMapping.ToPaymentDto(payment)
                };
                return Results.Ok(response);
            })
            .Produces<OrderPaymentResponse>()
            .WithTags("PaymentEndpoints");
    }
}
