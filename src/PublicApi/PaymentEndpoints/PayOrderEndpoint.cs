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

/// <summary>POST /api/orders/{orderId}/pay — authorize (hold) the order total with a card or a saved card (shopper-scoped).</summary>
public class PayOrderEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders/{orderId:int}/pay",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
        async (int orderId, PayOrderRequest request, ClaimsPrincipal user, IPaymentService service, CancellationToken ct) =>
            {
                var buyerId = CallerContext.BuyerId(user);
                var instruction = new AuthorizeInstruction(request.Card?.ToDetails(), request.SavedPaymentMethodId);
                var view = await service.AuthorizeAsync(buyerId, orderId, instruction, ct);
                return Results.Ok(view);
            })
            .Produces<OrderPaymentView>()
            .WithTags("PaymentEndpoints");
    }
}
