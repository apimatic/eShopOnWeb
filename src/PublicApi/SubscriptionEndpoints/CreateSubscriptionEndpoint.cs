using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// Subscribes the calling (JWT-authenticated) user to a Maxio plan. Ensures a Maxio customer
/// exists for the user (idempotent) and enrolls them; a repeated call for a plan the user is
/// already subscribed to returns the existing subscription rather than creating a duplicate.
/// </summary>
public class CreateSubscriptionEndpoint : IEndpoint<IResult, CreateSubscriptionRequest, ClaimsPrincipal, ISubscriptionAppService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/subscriptions",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (CreateSubscriptionRequest request, ClaimsPrincipal principal, ISubscriptionAppService subscriptionAppService, CancellationToken cancellationToken) =>
            {
                return await HandleAsync(request, principal, subscriptionAppService, cancellationToken);
            })
            .Produces<CreateSubscriptionResponse>()
            .WithTags("SubscriptionEndpoints");
    }

    public async Task<IResult> HandleAsync(CreateSubscriptionRequest request, ClaimsPrincipal principal, ISubscriptionAppService subscriptionAppService)
        => await HandleAsync(request, principal, subscriptionAppService, default);

    private async Task<IResult> HandleAsync(CreateSubscriptionRequest request, ClaimsPrincipal principal, ISubscriptionAppService subscriptionAppService, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.PlanHandle))
        {
            return Results.BadRequest("planHandle is required.");
        }

        var (subscription, created) = await subscriptionAppService.SubscribeCurrentUserAsync(principal, request.PlanHandle, cancellationToken);

        var response = new CreateSubscriptionResponse(request.CorrelationId())
        {
            Subscription = subscription,
            Created = created
        };

        return created
            ? Results.Created("api/my-subscriptions", response)
            : Results.Ok(response);
    }
}
