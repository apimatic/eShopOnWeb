using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// Subscribes the authenticated user to a plan. Ensures a billing customer exists for the user
/// and enrols them; idempotent per (user, plan) so a double-click never creates duplicates.
/// </summary>
public class CreateSubscriptionEndpoint : IEndpoint<IResult, CreateSubscriptionRequest, ISubscriptionService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/subscriptions",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (CreateSubscriptionRequest request, ClaimsPrincipal user, ISubscriptionService subscriptionService, CancellationToken cancellationToken) =>
            {
                request.Subscriber = SubscriberIdentityFactory.FromPrincipal(user);
                request.CancellationToken = cancellationToken;
                return await HandleAsync(request, subscriptionService);
            })
            .Produces<CreateSubscriptionResponse>(StatusCodes.Status201Created)
            .Produces<CreateSubscriptionResponse>(StatusCodes.Status200OK)
            .WithTags("SubscriptionEndpoints");
    }

    public async Task<IResult> HandleAsync(CreateSubscriptionRequest request, ISubscriptionService subscriptionService)
    {
        if (request.Subscriber is null)
        {
            return Results.Unauthorized();
        }

        var response = new CreateSubscriptionResponse(request.CorrelationId());
        var details = await subscriptionService.SubscribeAsync(request.Subscriber, request.PlanHandle, request.CancellationToken);

        response.Subscription = details.ToDto();
        response.Created = !details.AlreadyExisted;

        return details.AlreadyExisted
            ? Results.Ok(response)
            : Results.Created($"api/my-subscriptions/{details.Id}", response);
    }
}
