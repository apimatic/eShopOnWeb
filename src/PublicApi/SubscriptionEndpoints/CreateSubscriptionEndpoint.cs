using System.Threading;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Subscriptions;
using Microsoft.eShopWeb.Infrastructure.Identity;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// Subscribes the authenticated user to a plan (idempotent per user and plan)
/// </summary>
public class CreateSubscriptionEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/subscriptions",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (CreateSubscriptionRequest request, HttpContext http, UserManager<ApplicationUser> users,
                ISubscriptionBillingService billing, CancellationToken ct) =>
            {
                var user = await CallerIdentity.ResolveAsync(http, users);
                if (user is null)
                    return CallerIdentity.Fail(StatusCodes.Status401Unauthorized, "Unknown user.");

                var result = await billing.SubscribeAsync(user, request.PlanHandle, ct);
                var response = new CreateSubscriptionResponse(request.CorrelationId());
                switch (result.Outcome)
                {
                    case SubscribeOutcome.Created:
                    case SubscribeOutcome.Existing:
                        response.Created = result.Outcome == SubscribeOutcome.Created;
                        response.Subscription = SubscriptionDto.From(result.Subscription!);
                        return response.Created
                            ? Results.Created("api/my-subscriptions", response)
                            : Results.Ok(response);
                    case SubscribeOutcome.PlanNotAvailable:
                        return CallerIdentity.Fail(StatusCodes.Status400BadRequest, result.Message ?? "Plan not available.");
                    case SubscribeOutcome.InProgress:
                        return CallerIdentity.Fail(StatusCodes.Status409Conflict, result.Message ?? "Subscription in progress.");
                    case SubscribeOutcome.Refused:
                        return CallerIdentity.Fail(StatusCodes.Status422UnprocessableEntity, result.Message ?? "The billing system refused the request.");
                    default:
                        return CallerIdentity.Fail(StatusCodes.Status503ServiceUnavailable, result.Message ?? "The billing system is unavailable.");
                }
            })
            .Produces<CreateSubscriptionResponse>(StatusCodes.Status201Created)
            .Produces<CreateSubscriptionResponse>()
            .WithTags("SubscriptionEndpoints");
    }
}
