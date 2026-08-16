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

public class PayOrderRequest
{
    /// <summary>Card details for a one-off payment. Provide this or <see cref="SavedPaymentMethodId"/>, not both.</summary>
    public CardInfo? Card { get; set; }

    /// <summary>Id of one of the shopper's saved cards to pay with instead of inline card details.</summary>
    public int? SavedPaymentMethodId { get; set; }
}

/// <summary>
/// Authorizes (places a hold equal to the order total on) the shopper's own order, using either
/// inline card details or one of their saved cards. Does not capture — the money is only held.
/// Idempotent: a repeated call never authorizes twice.
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
                CancellationToken cancellationToken) =>
            {
                var buyerId = user.GetBuyerId();
                if (string.IsNullOrEmpty(buyerId))
                {
                    return Results.Unauthorized();
                }

                var card = request.Card?.ToCardDetails();
                var view = await paymentService.AuthorizeAsync(buyerId, orderId, card,
                    request.SavedPaymentMethodId, cancellationToken);

                return Results.Ok(view);
            })
            .Produces(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status401Unauthorized)
            .WithTags("OrderEndpoints");
    }
}
