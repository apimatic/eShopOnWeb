using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.PublicApi.PaymentModels;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

/// <summary>
/// Authorizes (holds) the order total at PayPal, paying with one-off card details or one of the shopper's saved
/// cards. No money is taken yet. Idempotent: a double-click never authorizes the shopper twice.
/// </summary>
public class PayOrderEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders/{orderId}/pay",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
            async (int orderId, PayOrderRequest request, ClaimsPrincipal user,
                IOrderPaymentService orderPaymentService, IPaymentSettings paymentSettings, CancellationToken cancellationToken) =>
            {
                var buyerId = user.FindFirstValue(ClaimTypes.Name);
                if (string.IsNullOrEmpty(buyerId))
                {
                    return Results.Unauthorized();
                }

                var hasCard = request?.Card != null;
                var hasSaved = request?.PaymentMethodId != null;
                if (hasCard == hasSaved)
                {
                    return Results.BadRequest(new { error = "Provide exactly one of 'card' (one-off) or 'paymentMethodId' (saved card)." });
                }

                var card = hasCard ? PaymentMapping.ToCardDetails(request!.Card!) : null;
                var order = await orderPaymentService.PayOrderAsync(buyerId, orderId, card, request!.PaymentMethodId, cancellationToken);

                var dto = PaymentMapping.ToOrderDto(order, paymentSettings.Currency);
                return Results.Ok(new { orderId = order.Id, order = dto });
            })
            .Produces(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .WithTags("OrderEndpoints");
    }
}
