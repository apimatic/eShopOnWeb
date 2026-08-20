using System.Security.Claims;
using System.Threading;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Billing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public sealed class ListSubscriptionPlansEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/subscription-plans",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async (
                ClaimsPrincipal principal,
                ISubscriptionBillingService billing,
                CancellationToken cancellationToken) =>
            {
                if (SubscriptionEndpointResults.UserId(principal) is null)
                {
                    return Results.Unauthorized();
                }

                try
                {
                    return Results.Ok(await billing.GetPlansAsync(cancellationToken));
                }
                catch (BillingProviderException ex)
                {
                    return SubscriptionEndpointResults.From(ex);
                }
            })
            .Produces(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status502BadGateway)
            .ProducesProblem(StatusCodes.Status503ServiceUnavailable)
            .WithTags("SubscriptionEndpoints");
    }
}
