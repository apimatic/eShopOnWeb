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
/// Subscribe the authenticated shopper to a plan (idempotent per shopper)
/// </summary>
public class CreateSubscriptionEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/subscriptions",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (CreateSubscriptionRequest request, ClaimsPrincipal user, UserManager<ApplicationUser> users, ISubscriptionBilling billing, CancellationToken cancellationToken) =>
            {
                return await HandleAsync(request, user, users, billing, cancellationToken);
            })
            .Produces<CreateSubscriptionResponse>()
            .WithTags("SubscriptionEndpoints");
    }

    public async Task<IResult> HandleAsync(CreateSubscriptionRequest request, ClaimsPrincipal principal, UserManager<ApplicationUser> users, ISubscriptionBilling billing, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.PlanHandle))
            return Results.Problem("planHandle is required.", statusCode: StatusCodes.Status400BadRequest);

        var appUser = await SubscriptionHttp.ResolveUserAsync(principal, users);
        if (appUser == null || string.IsNullOrWhiteSpace(appUser.Email))
            return Results.Unauthorized();

        var response = new CreateSubscriptionResponse(request.CorrelationId());
        try
        {
            var result = await billing.SubscribeAsync(appUser.Id, appUser.Email, request.PlanHandle.Trim(), cancellationToken);
            response.Subscription = result.Subscription == null ? null : SubscriptionDto.From(result.Subscription);
            response.Status = result.Outcome switch
            {
                SubscribeOutcome.Created => "created",
                SubscribeOutcome.AlreadySubscribed => "already-subscribed",
                _ => "pending-confirmation"
            };

            return result.Outcome switch
            {
                SubscribeOutcome.Created => Results.Created("api/my-subscriptions", response),
                SubscribeOutcome.AlreadySubscribed => Results.Ok(response),
                _ => Results.Accepted("api/my-subscriptions", response)
            };
        }
        catch (BillingException ex)
        {
            return SubscriptionHttp.ToResult(ex);
        }
    }
}
