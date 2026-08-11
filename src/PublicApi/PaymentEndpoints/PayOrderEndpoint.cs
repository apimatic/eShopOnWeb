using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces.Payments;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.PaymentEndpoints;

/// <summary>
/// Authorizes (holds) the order total, paying with either a one-off card or one of the shopper's saved
/// cards. The money is held, not taken. Idempotent in effect: a double-click never authorizes twice.
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
                IOrderPaymentService service,
                CancellationToken ct) =>
            {
                var buyerId = PaymentEndpointHelpers.GetBuyerId(user);
                if (string.IsNullOrEmpty(buyerId)) return Results.Unauthorized();

                var instruction = new PaymentInstruction
                {
                    Card = request.Card?.ToCardDetails(),
                    SavedCardId = request.SavedCardId
                };

                var summary = await service.PayAsync(buyerId, orderId, instruction, ct);
                return Results.Ok(summary);
            })
            .Produces<OrderPaymentSummary>()
            .WithTags("PaymentEndpoints");
    }
}
