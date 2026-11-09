using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.PublicApi.Payments;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

/// <summary>
/// Shows the exact amount (fee and total) the shopper pays with the chosen payment method
/// </summary>
public class CheckoutOrderEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders/{orderId}/checkout",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int orderId, PaymentMethodChoiceRequest request, ClaimsPrincipal user, OrderPaymentService service, CancellationToken ct) =>
            {
                var result = await service.CheckoutAsync(orderId, user.Email(), request.PaymentMethodId, ct);
                return Results.Ok(result);
            })
            .Produces<CheckoutDto>()
            .WithTags("OrderEndpoints");
    }
}
