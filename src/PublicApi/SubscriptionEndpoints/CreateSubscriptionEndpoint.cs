using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.Infrastructure.Identity;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// Subscribes the calling user to a plan. Idempotent per (user, plan): repeating the request
/// returns the existing subscription instead of creating another.
/// </summary>
public class CreateSubscriptionEndpoint : IEndpoint<IResult, CreateSubscriptionRequest, ISubscriptionBillingService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/subscriptions",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (CreateSubscriptionRequest? request, ClaimsPrincipal principal, UserManager<ApplicationUser> userManager,
                ISubscriptionBillingService billingService, HttpContext httpContext) =>
            {
                var user = await BillingUserResolver.ResolveAsync(principal, userManager);
                if (user is null)
                {
                    return Results.Unauthorized();
                }

                return await HandleAsync(request ?? new CreateSubscriptionRequest(), user, billingService, httpContext.RequestAborted);
            })
            .Produces<CreateSubscriptionResponse>(StatusCodes.Status201Created)
            .Produces<CreateSubscriptionResponse>(StatusCodes.Status200OK)
            .Produces<CreateSubscriptionResponse>(StatusCodes.Status202Accepted)
            .WithTags("SubscriptionEndpoints");
    }

    /// <summary>The caller's identity is required; this overload exists for the endpoint contract only.</summary>
    public Task<IResult> HandleAsync(CreateSubscriptionRequest request, ISubscriptionBillingService billingService) =>
        Task.FromResult(Results.Unauthorized());

    public async Task<IResult> HandleAsync(CreateSubscriptionRequest request, BillingUser user,
        ISubscriptionBillingService billingService, CancellationToken cancellationToken)
    {
        var response = new CreateSubscriptionResponse(request.CorrelationId());

        var result = await billingService.SubscribeAsync(user, request.PlanHandle, cancellationToken);

        response.Outcome = result.Outcome.ToString();
        response.PlanHandle = result.PlanHandle;
        response.Reference = result.Reference;
        response.Subscription = result.Subscription?.ToDto();

        return result.Outcome switch
        {
            SubscribeOutcome.Created => Results.Created("api/my-subscriptions", response),
            SubscribeOutcome.PendingConfirmation => Results.Accepted("api/my-subscriptions", response),
            _ => Results.Ok(response)
        };
    }
}
