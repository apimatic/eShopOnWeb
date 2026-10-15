using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.Infrastructure.Identity;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// Subscribe the authenticated user to a plan (idempotent per user and plan)
/// </summary>
public class CreateSubscriptionEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/subscriptions",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (CreateSubscriptionRequest request, ClaimsPrincipal user, ISubscriptionService subscriptionService,
                UserManager<ApplicationUser> userManager, CancellationToken cancellationToken) =>
            {
                return await HandleAsync(request, user, subscriptionService, userManager, cancellationToken);
            })
            .Produces<CreateSubscriptionResponse>()
            .WithTags("SubscriptionEndpoints");
    }

    public async Task<IResult> HandleAsync(CreateSubscriptionRequest request, ClaimsPrincipal user,
        ISubscriptionService subscriptionService, UserManager<ApplicationUser> userManager, CancellationToken cancellationToken)
    {
        var userName = SubscriptionResults.UserName(user);
        if (string.IsNullOrWhiteSpace(userName))
            return Results.Unauthorized();
        if (string.IsNullOrWhiteSpace(request.PlanHandle))
            return Results.Problem("planHandle is required.", statusCode: StatusCodes.Status400BadRequest);

        var identityUser = await userManager.FindByNameAsync(userName);
        var email = identityUser?.Email ?? (userName.Contains('@') ? userName : null);
        if (string.IsNullOrWhiteSpace(email))
            return Results.Problem("The account has no email address.", statusCode: StatusCodes.Status400BadRequest);

        try
        {
            var result = await subscriptionService.SubscribeAsync(new Subscriber(userName, email), request.PlanHandle.Trim(), cancellationToken);
            var response = new CreateSubscriptionResponse(request.CorrelationId())
            {
                Subscription = SubscriptionResults.ToDto(result.Subscription),
                AlreadySubscribed = result.AlreadySubscribed
            };
            return result.AlreadySubscribed
                ? Results.Ok(response)
                : Results.Created("api/my-subscriptions", response);
        }
        catch (SubscriptionException ex)
        {
            return SubscriptionResults.Failure(ex);
        }
    }
}
