using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.PublicApi.Payments;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.PaymentEndpoints;

/// <summary>
/// Refunds a captured payment, in full or in part, using a caller-supplied idempotency key.
/// </summary>
public class RefundOrderEndpoint : IEndpoint<IResult, RefundOrderRequest, PaymentService, ClaimsPrincipal>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders/{orderId}/refunds",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int orderId, RefundOrderRequest request, PaymentService payments, ClaimsPrincipal user) =>
            {
                request.OrderId = orderId;
                return await HandleAsync(request, payments, user);
            })
            .Produces<RefundOrderResponse>()
            .WithTags("OrderEndpoints");
    }

    public async Task<IResult> HandleAsync(RefundOrderRequest request, PaymentService payments, ClaimsPrincipal user)
    {
        var owner = user.Identity?.Name;
        if (string.IsNullOrWhiteSpace(owner))
        {
            return Results.Unauthorized();
        }

        var response = await payments.RefundAsync(owner, user.IsInRole(BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS), request, default);
        return Results.Ok(response);
    }
}
