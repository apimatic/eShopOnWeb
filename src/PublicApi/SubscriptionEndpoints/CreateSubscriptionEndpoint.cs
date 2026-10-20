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
using Microsoft.eShopWeb.PublicApi.SubscriptionBilling;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// Subscribe the calling user to a plan. Idempotent per user and plan: repeating the request never
/// creates a second Maxio customer or subscription.
/// </summary>
public class CreateSubscriptionEndpoint : IEndpoint<IResult, SubscribeCommand, ISubscriptionBillingService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/subscriptions",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (CreateSubscriptionRequest request, ClaimsPrincipal principal, UserManager<ApplicationUser> userManager,
                ISubscriptionBillingService billingService, CancellationToken cancellationToken) =>
            {
                var user = await BillingUserResolver.ResolveAsync(principal, userManager);
                if (user is null)
                    return Results.Unauthorized();

                return await HandleAsync(new SubscribeCommand(request, user, cancellationToken), billingService);
            })
            .Produces<CreateSubscriptionResponse>(StatusCodes.Status201Created)
            .Produces<CreateSubscriptionResponse>(StatusCodes.Status200OK)
            .Produces<CreateSubscriptionResponse>(StatusCodes.Status202Accepted)
            .WithTags("SubscriptionEndpoints");
    }

    public async Task<IResult> HandleAsync(SubscribeCommand command, ISubscriptionBillingService billingService)
    {
        var response = new CreateSubscriptionResponse(command.Request.CorrelationId());

        var result = await billingService.SubscribeAsync(command.User, command.Request.ProductHandle ?? string.Empty, command.CancellationToken);

        response.Reference = result.Reference;
        response.Subscription = result.Subscription is null ? null : SubscriptionDto.From(result.Subscription);

        switch (result.Outcome)
        {
            case SubscribeOutcome.Created:
                response.Status = "created";
                return Results.Created("api/my-subscriptions", response);
            case SubscribeOutcome.AlreadySubscribed:
                response.Status = "already_subscribed";
                return Results.Ok(response);
            default:
                response.Status = "pending";
                response.Message = "The subscription request was sent but could not be confirmed yet. Repeat the request to check its status; it will not subscribe you twice.";
                return Results.Accepted("api/my-subscriptions", response);
        }
    }
}

/// <summary>The request body plus the caller identity resolved from the token.</summary>
public record SubscribeCommand(CreateSubscriptionRequest Request, BillingUser User, CancellationToken CancellationToken);
