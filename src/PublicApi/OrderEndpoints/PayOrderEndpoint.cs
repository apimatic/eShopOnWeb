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

/// <summary>Pays for an order by putting a hold (authorization) on the total. Card or saved card.</summary>
public class PayOrderRequest
{
    /// <summary>Card details for a one-off payment. Omit when paying with a saved card.</summary>
    public CardModel? Card { get; set; }

    /// <summary>The id of one of the shopper's saved cards to pay with. Omit when passing card details.</summary>
    public int? SavedPaymentMethodId { get; set; }
}

/// <summary>
/// POST /api/orders/{orderId}/pay — authorizes the order total: holds the money with PayPal but does
/// not take it. Idempotent: a double-click never authorizes twice.
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
                IOrderPaymentService paymentService,
                CancellationToken cancellationToken) =>
            {
                var buyerId = CurrentUser.BuyerId(user);
                var card = request.Card?.ToCardDetails();

                var order = await paymentService.AuthorizeAsync(
                    buyerId, orderId, card, request.SavedPaymentMethodId, cancellationToken);

                return Results.Ok(new
                {
                    orderId = order.Id,
                    status = order.Status.ToString(),
                    order = order.ToDto()
                });
            })
            .Produces(StatusCodes.Status200OK)
            .WithTags("OrderEndpoints");
    }
}
