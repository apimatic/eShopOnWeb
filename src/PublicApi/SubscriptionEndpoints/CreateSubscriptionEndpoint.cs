using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Subscriptions;
using MinimalApi.Endpoint;
using Swashbuckle.AspNetCore.Annotations;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// Subscribes the authenticated shopper to a plan. Idempotent: ensures a single Maxio customer for
/// the user and never creates a second live subscription to the same plan, so repeated (e.g.
/// double-clicked) requests return the existing subscription rather than duplicating it.
/// </summary>
public class CreateSubscriptionEndpoint : IEndpoint<IResult, CreateSubscriptionRequest, IMaxioSubscriptionService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/subscriptions",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
            [SwaggerOperation(
                Summary = "Subscribes the current user to a plan",
                Description = "Enrolls the authenticated shopper in the given plan (idempotent)",
                OperationId = "subscriptions.create",
                Tags = new[] { "SubscriptionEndpoints" })]
            async (CreateSubscriptionRequest request, ClaimsPrincipal user, IMaxioSubscriptionService subscriptionService) =>
            {
                var email = user.FindFirstValue(ClaimTypes.Name);
                if (string.IsNullOrWhiteSpace(email))
                {
                    return Results.Unauthorized();
                }

                request.SubscriberEmail = email;
                return await HandleAsync(request, subscriptionService);
            })
            .Produces<CreateSubscriptionResponse>(StatusCodes.Status201Created)
            .Produces<CreateSubscriptionResponse>(StatusCodes.Status200OK)
            .WithTags("SubscriptionEndpoints");
    }

    public async Task<IResult> HandleAsync(CreateSubscriptionRequest request, IMaxioSubscriptionService subscriptionService)
    {
        if (string.IsNullOrWhiteSpace(request.PlanHandle))
        {
            return Results.BadRequest(new { message = "A 'planHandle' is required to subscribe." });
        }

        var subscriber = SubscriberIdentity.FromEmail(request.SubscriberEmail!);
        var result = await subscriptionService.SubscribeAsync(subscriber, request.PlanHandle.Trim());

        var response = new CreateSubscriptionResponse(request.CorrelationId())
        {
            Subscription = result.Subscription.ToDto(),
            AlreadySubscribed = result.AlreadySubscribed,
            Message = result.AlreadySubscribed
                ? $"You are already subscribed to '{result.Subscription.PlanName ?? request.PlanHandle}'."
                : $"Successfully subscribed to '{result.Subscription.PlanName ?? request.PlanHandle}'.",
        };

        return result.AlreadySubscribed
            ? Results.Ok(response)
            : Results.Created($"api/my-subscriptions/{result.Subscription.Id}", response);
    }
}
