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

/// <summary>Pay with raw card details, or with a saved card (exactly one of the two).</summary>
public class PayOrderRequest
{
    public CardInput? Card { get; set; }
    public int? SavedPaymentMethodId { get; set; }
}

/// <summary>POST /api/orders/{orderId}/pay — authorizes (holds) the order total. Idempotent.</summary>
public class PayOrderEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders/{orderId:int}/pay",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async (
                int orderId,
                PayOrderRequest request,
                IOrderPaymentService service,
                ClaimsPrincipal user,
                CancellationToken ct) =>
            await PaymentApiSupport.ExecuteAsync(async () =>
            {
                var buyerId = PaymentApiSupport.RequireBuyerId(user);
                var card = request.Card is null ? null : PaymentApiSupport.ToCardDetails(request.Card);
                var instruction = new PayInstruction(card, request.SavedPaymentMethodId);
                var payment = await service.PayAsync(buyerId, orderId, instruction, ct);
                return Results.Ok(PaymentMappings.ToState(payment));
            }))
            .Produces<PaymentStateResponse>()
            .WithTags("Payments");
    }
}
