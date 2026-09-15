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

public class PayOrderRequest : BaseRequest
{
    /// <summary>Card details for a one-off payment. Provide this OR <see cref="SavedPaymentMethodId"/>.</summary>
    public CardRequest? Card { get; set; }

    /// <summary>The id of one of the shopper's saved cards to pay with instead of a raw card.</summary>
    public int? SavedPaymentMethodId { get; set; }
}

/// <summary>
/// POST /api/orders/{orderId}/pay — authorize (hold) the order total. Does not take the money yet.
/// Idempotent in effect: a double-click never authorizes twice.
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
                CancellationToken ct) =>
            {
                var buyerId = CallerIdentity.BuyerId(user);
                var instruction = new PayInstruction(request.Card?.ToCardDetails(), request.SavedPaymentMethodId);
                var payment = await paymentService.AuthorizeAsync(buyerId, orderId, instruction, ct);
                return Results.Ok(PaymentDto.From(payment));
            })
            .Produces<PaymentDto>()
            .WithTags("PaymentEndpoints");
    }
}
