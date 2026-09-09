using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.Infrastructure.Identity;
using Microsoft.eShopWeb.PublicApi.Maxio;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// Subscribes the authenticated user to a plan. Idempotent: repeating the
/// request for a plan the user already subscribes to returns the existing
/// subscription instead of creating a second one.
/// </summary>
public class CreateSubscriptionEndpoint : IEndpoint<IResult, CreateSubscriptionRequest, ApplicationUser, IMaxioSubscriptionService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/subscriptions",
            async (CreateSubscriptionRequest request, ClaimsPrincipal principal,
                   UserManager<ApplicationUser> userManager, IMaxioSubscriptionService subscriptionService,
                   CancellationToken cancellationToken) =>
            {
                var user = await principal.ResolveApplicationUserAsync(userManager);
                if (user is null)
                {
                    return Results.Unauthorized();
                }
                return await HandleAsync(request, user, subscriptionService, cancellationToken);
            })
           .Produces<CreateSubscriptionResponse>()
           .RequireAuthorization()
           .WithTags("SubscriptionEndpoints");
    }

    public Task<IResult> HandleAsync(CreateSubscriptionRequest request, ApplicationUser user, IMaxioSubscriptionService subscriptionService)
    {
        return HandleAsync(request, user, subscriptionService, CancellationToken.None);
    }

    public async Task<IResult> HandleAsync(CreateSubscriptionRequest request, ApplicationUser user,
        IMaxioSubscriptionService subscriptionService, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request?.PlanHandle))
        {
            return Results.Problem(detail: "PlanHandle is required.", statusCode: StatusCodes.Status400BadRequest);
        }

        try
        {
            var subscription = await subscriptionService.SubscribeAsync(user.ToMaxioUserInfo(), request.PlanHandle.Trim(), cancellationToken);
            return Results.Ok(new CreateSubscriptionResponse(request.CorrelationId()) { Subscription = subscription });
        }
        catch (MaxioBillingException ex)
        {
            return Results.Problem(detail: ex.Message, statusCode: ex.RecommendedHttpStatusCode);
        }
    }
}
