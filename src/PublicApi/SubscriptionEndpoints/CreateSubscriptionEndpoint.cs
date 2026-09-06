using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Subscriptions;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// Subscribes the caller to a plan.
/// </summary>
/// <remarks>
/// The call is idempotent per (caller, plan): a repeated or double-clicked request returns the
/// subscription the first one created, with <c>created: false</c> and 200 instead of 201.
/// </remarks>
public class CreateSubscriptionEndpoint : IEndpoint<IResult, CreateSubscriptionContext, ISubscriptionService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/subscriptions",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (CreateSubscriptionRequest request,
             ClaimsPrincipal user,
             ISubscriberResolver subscriberResolver,
             ISubscriptionService subscriptionService,
             CancellationToken cancellationToken) =>
            {
                var subscriber = await subscriberResolver.ResolveAsync(user, cancellationToken);
                var context = new CreateSubscriptionContext(request, subscriber, cancellationToken);

                return await HandleAsync(context, subscriptionService);
            })
            .Produces<CreateSubscriptionResponse>(StatusCodes.Status201Created)
            .Produces<CreateSubscriptionResponse>(StatusCodes.Status200OK)
            .WithTags("SubscriptionEndpoints");
    }

    public async Task<IResult> HandleAsync(CreateSubscriptionContext context, ISubscriptionService subscriptionService)
    {
        var response = new CreateSubscriptionResponse(context.Request.CorrelationId());

        var result = await subscriptionService.SubscribeAsync(
            context.Subscriber, context.Request.PlanHandle, context.CancellationToken);

        response.Created = result.Created;
        response.Subscription = result.Subscription.ToDto();

        return result.Created
            ? Results.Created("/api/my-subscriptions", response)
            : Results.Ok(response);
    }
}
