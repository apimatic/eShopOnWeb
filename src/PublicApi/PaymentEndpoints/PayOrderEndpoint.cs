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
/// The pay request: either raw card details for a one-off payment, or the id of one of the
/// shopper's saved cards to pay with instead.
/// </summary>
public class PayOrderRequest
{
    public CardRequest? Card { get; set; }
    public int? SavedPaymentMethodId { get; set; }
}

/// <summary>
/// Authorizes an order's total — puts a hold on the money without taking it. Shopper-scoped:
/// acts only on the caller's own order. Idempotent: a double-click never authorizes twice.
/// </summary>
public class PayOrderEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders/{orderId}/pay",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async (
                int orderId,
                PayOrderRequest request,
                ClaimsPrincipal user,
                IPaymentService paymentService,
                IPaymentSettings settings,
                CancellationToken ct) =>
            {
                var buyerId = user.GetBuyerId();
                if (buyerId is null) return Results.Unauthorized();

                var card = request.Card?.ToCardDetails();
                var result = await paymentService.AuthorizeOrderAsync(buyerId, orderId, card,
                    request.SavedPaymentMethodId, ct);
                if (!result.IsSuccess) return result.ToProblem();

                var dto = result.Value.ToDto(settings.Currency);
                return Results.Ok(new { orderId = result.Value.Id, order = dto });
            })
            .Produces(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status404NotFound)
            .Produces(StatusCodes.Status422UnprocessableEntity)
            .WithTags("OrderEndpoints");
    }
}
