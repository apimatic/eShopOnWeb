using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.PublicApi.Banking;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.BankEndpoints;

/// <summary>Cancel an order paid by bank (with a full refund), and mark one as shipped.</summary>
public class OrderBankEndpoints : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders/{orderId}/cancel",
                [Authorize(AuthenticationSchemes = Shopper.Scheme)] async (int orderId, ClaimsPrincipal user,
                    BankPaymentService payments) => Results.Ok(await payments.CancelAsync(Shopper.Id(user), orderId)))
            .WithTags("BankEndpoints");

        app.MapPost("api/orders/{orderId}/ship",
                [Authorize(Roles = Shopper.Admins, AuthenticationSchemes = Shopper.Scheme)] async (int orderId,
                    BankPaymentService payments) => Results.Ok(await payments.MarkShippedAsync(orderId)))
            .WithTags("BankEndpoints");
    }
}
