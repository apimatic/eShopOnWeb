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
/// Subscribe the calling shopper to a plan. Idempotent: repeating the request returns the existing subscription.
/// </summary>
public class CreateSubscriptionEndpoint : IEndpoint<IResult, CreateSubscriptionRequest, ISubscriptionService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/subscriptions",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (CreateSubscriptionRequest request, ISubscriptionService subscriptionService, ClaimsPrincipal user,
                UserManager<ApplicationUser> userManager, CancellationToken cancellationToken) =>
            {
                var shopper = await ShopperIdentity.ResolveAsync(user, userManager);
                if (shopper is null)
                {
                    return Results.Unauthorized();
                }
                request.Shopper = shopper;
                return await HandleAsync(request, subscriptionService, cancellationToken);
            })
            .Produces<CreateSubscriptionResponse>(StatusCodes.Status201Created)
            .Produces<CreateSubscriptionResponse>()
            .WithTags("SubscriptionEndpoints");
    }

    public Task<IResult> HandleAsync(CreateSubscriptionRequest request, ISubscriptionService subscriptionService) =>
        HandleAsync(request, subscriptionService, CancellationToken.None);

    public async Task<IResult> HandleAsync(CreateSubscriptionRequest request, ISubscriptionService subscriptionService,
        CancellationToken cancellationToken)
    {
        var response = new CreateSubscriptionResponse(request.CorrelationId());

        if (request.Shopper is not { } shopper)
        {
            return Results.Unauthorized();
        }

        if (string.IsNullOrWhiteSpace(request.PlanHandle))
        {
            return Results.BadRequest(new { StatusCode = StatusCodes.Status400BadRequest, Message = "PlanHandle is required." });
        }

        var result = await subscriptionService.SubscribeAsync(
            new SubscribeCommand(shopper.UserName, shopper.Email, request.PlanHandle.Trim()), cancellationToken);

        response.Subscription = SubscriptionDto.From(result.Subscription);
        response.Created = result.Created;
        return result.Created
            ? Results.Created("api/my-subscriptions", response)
            : Results.Ok(response);
    }
}
