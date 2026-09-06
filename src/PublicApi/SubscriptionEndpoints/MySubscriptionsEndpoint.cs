using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.Infrastructure.Identity;
using Microsoft.eShopWeb.PublicApi.Maxio;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>Lists Maxio subscriptions belonging to the authenticated shopper.</summary>
public sealed class MySubscriptionsEndpoint : IEndpoint<IResult, IMaxioBillingService>
{
    // The route additionally resolves the JWT subject, which is deliberately not supplied by a service parameter.
    public Task<IResult> HandleAsync(IMaxioBillingService billing)
        => Task.FromResult<IResult>(Results.Unauthorized());

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/my-subscriptions", async (
            ClaimsPrincipal principal,
            UserManager<ApplicationUser> userManager,
            IMaxioBillingService billing,
            CancellationToken cancellationToken) =>
        {
            var shopper = await SubscriptionEndpointHelpers.GetShopperAsync(principal, userManager);
            if (shopper is null)
                return Results.Unauthorized();

            try
            {
                return Results.Ok(await billing.GetSubscriptionsAsync(shopper, cancellationToken));
            }
            catch (MaxioApiException exception)
            {
                return SubscriptionEndpointHelpers.MaxioFailure(exception);
            }
        })
        .RequireAuthorization(new AuthorizeAttribute { AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme })
        .Produces<SubscriptionDto[]>()
        .ProducesProblem(StatusCodes.Status502BadGateway)
        .WithTags("SubscriptionEndpoints");
    }
}
