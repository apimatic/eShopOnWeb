using System.Linq;
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
using Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints.Maxio;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// Lists the authenticated user's subscriptions in Maxio.
/// </summary>
public class MySubscriptionsListEndpoint : IEndpoint<IResult, IMaxioSubscriptionService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/my-subscriptions",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
            async (ClaimsPrincipal principal,
                   UserManager<ApplicationUser> userManager,
                   IMaxioSubscriptionService service,
                   CancellationToken ct) =>
            {
                var user = await MaxioUserResolver.ResolveAsync(principal, userManager);
                if (user is null)
                    return Results.Unauthorized();
                return await HandleAsync(user, service, ct);
            })
            .Produces<MySubscriptionsResponse>()
            .ProducesProblem(StatusCodes.Status502BadGateway)
            .WithTags("SubscriptionEndpoints");
    }

    /// <summary>
    /// Required by <see cref="IEndpoint{TResult,TDependency}"/>. The real work needs the caller's identity,
    /// which is resolved from the JWT in <see cref="AddRoute"/>; a call without it is unauthenticated.
    /// </summary>
    public Task<IResult> HandleAsync(IMaxioSubscriptionService service) => Task.FromResult(Results.Unauthorized());

    public async Task<IResult> HandleAsync(MaxioUserContext user, IMaxioSubscriptionService service, CancellationToken ct)
    {
        try
        {
            var subs = await service.GetMySubscriptionsAsync(user, ct);
            var response = new MySubscriptionsResponse
            {
                Subscriptions = subs.Select(SubscriptionDto.From).ToList()
            };
            return Results.Ok(response);
        }
        catch (MaxioIntegrationException ex)
        {
            return SubscriptionResults.FromError(ex);
        }
    }
}
