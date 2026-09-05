using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Maxio;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// Subscribes the current eShopOnWeb user to a Maxio plan: ensures a Maxio customer exists for
/// them (idempotent), then enrolls them (idempotent - a double-click reuses any live
/// subscription to the same plan rather than creating a duplicate).
/// </summary>
public class CreateSubscriptionEndpoint : IEndpoint<IResult, CreateSubscriptionRequest, HttpContext, IMaxioSubscriptionService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/subscriptions",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (CreateSubscriptionRequest request, HttpContext httpContext, IMaxioSubscriptionService subscriptionService) =>
            {
                return await HandleAsync(request, httpContext, subscriptionService);
            })
            .Produces<CreateSubscriptionResponse>()
            .Produces(StatusCodes.Status400BadRequest)
            .Produces(StatusCodes.Status401Unauthorized)
            .WithTags("SubscriptionEndpoints");
    }

    public async Task<IResult> HandleAsync(CreateSubscriptionRequest request, HttpContext httpContext, IMaxioSubscriptionService subscriptionService)
    {
        if (string.IsNullOrWhiteSpace(request.PlanHandle))
        {
            return Results.BadRequest("planHandle is required.");
        }

        var user = await CurrentUserResolver.GetCurrentUserAsync(httpContext);
        if (user is null)
        {
            return Results.Unauthorized();
        }

        var (firstName, lastName) = CurrentUserResolver.DeriveDisplayName(user);
        var email = user.Email ?? user.UserName!;

        var subscription = await subscriptionService.SubscribeAsync(user.Id, email, firstName, lastName, request.PlanHandle);

        var response = new CreateSubscriptionResponse(request.CorrelationId())
        {
            Subscription = new SubscriptionDto
            {
                SubscriptionId = subscription.SubscriptionId,
                State = subscription.State,
                NextBillingAt = subscription.NextBillingAt,
                Plan = new SubscriptionPlanDto
                {
                    Handle = subscription.Plan.Handle,
                    Name = subscription.Plan.Name,
                    Price = subscription.Plan.Price,
                    IntervalCount = subscription.Plan.IntervalCount,
                    IntervalUnit = subscription.Plan.IntervalUnit
                }
            }
        };

        return Results.Ok(response);
    }
}
