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
using Microsoft.eShopWeb.PublicApi.Billing;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// Subscribe the authenticated shopper to a plan. Idempotent per (user, plan): a repeat call returns
/// the existing subscription with 200 instead of creating another.
/// </summary>
public class CreateSubscriptionEndpoint : IEndpoint<IResult, CreateSubscriptionRequest, BillingCaller?, SubscriptionBillingService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/subscriptions",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (CreateSubscriptionRequest request, ClaimsPrincipal user, UserManager<ApplicationUser> userManager,
             SubscriptionBillingService billing, CancellationToken cancellationToken) =>
            {
                var userName = BillingEndpointSupport.UserNameOf(user);
                BillingCaller? caller = null;
                if (userName is not null)
                {
                    var account = await userManager.FindByNameAsync(userName);
                    caller = new BillingCaller(userName, account?.Email);
                }

                return await HandleAsync(request, caller, billing, cancellationToken);
            })
            .Produces<CreateSubscriptionResponse>(StatusCodes.Status201Created)
            .Produces<CreateSubscriptionResponse>(StatusCodes.Status200OK)
            .Produces<CreateSubscriptionResponse>(StatusCodes.Status202Accepted)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .ProducesProblem(StatusCodes.Status422UnprocessableEntity)
            .ProducesProblem(StatusCodes.Status502BadGateway)
            .ProducesProblem(StatusCodes.Status503ServiceUnavailable)
            .ProducesProblem(StatusCodes.Status504GatewayTimeout)
            .WithTags(BillingEndpointSupport.Tag);
    }

    public Task<IResult> HandleAsync(CreateSubscriptionRequest request, BillingCaller? caller, SubscriptionBillingService billing) =>
        HandleAsync(request, caller, billing, CancellationToken.None);

    public async Task<IResult> HandleAsync(CreateSubscriptionRequest request, BillingCaller? caller, SubscriptionBillingService billing, CancellationToken cancellationToken)
    {
        var response = new CreateSubscriptionResponse(request.CorrelationId());
        try
        {
            if (caller is null) throw BillingException.Unauthenticated();

            var result = await billing.SubscribeAsync(caller, request.PlanHandle, cancellationToken);
            response.Subscription = result.Subscription;
            switch (result.Outcome)
            {
                case SubscribeOutcome.Created:
                    response.Outcome = "created";
                    return Results.Created("api/my-subscriptions", response);
                case SubscribeOutcome.Existing:
                    response.Outcome = "existing";
                    return Results.Ok(response);
                default:
                    response.Outcome = "unknown";
                    return Results.Accepted("api/my-subscriptions", response);
            }
        }
        catch (BillingException ex)
        {
            return BillingEndpointSupport.Problem(ex, response.CorrelationId());
        }
    }
}
