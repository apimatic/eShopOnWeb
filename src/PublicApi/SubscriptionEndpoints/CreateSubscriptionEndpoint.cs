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
using Microsoft.eShopWeb.Infrastructure.Maxio;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// Subscribes the signed-in shopper to a plan in Maxio Advanced Billing.
/// Idempotent: subscribing again to a plan the shopper is already on returns the existing subscription.
/// </summary>
public class CreateSubscriptionEndpoint : IEndpoint<IResult, ClaimsPrincipal, CreateSubscriptionRequest, UserManager<ApplicationUser>>
{
    private readonly IMaxioSubscriptionService _subscriptionService;

    public CreateSubscriptionEndpoint(IMaxioSubscriptionService subscriptionService)
    {
        _subscriptionService = subscriptionService;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/subscriptions",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (CreateSubscriptionRequest request, ClaimsPrincipal user, UserManager<ApplicationUser> userManager) =>
            {
                return await HandleAsync(user, request, userManager);
            })
            .Produces<CreateSubscriptionResponse>()
            .WithTags("SubscriptionEndpoints");
    }

    public async Task<IResult> HandleAsync(ClaimsPrincipal user, CreateSubscriptionRequest request, UserManager<ApplicationUser> userManager)
    {
        var response = new CreateSubscriptionResponse(request.CorrelationId());

        var userName = user.Identity?.Name;
        if (string.IsNullOrWhiteSpace(userName))
        {
            return Results.Unauthorized();
        }

        if (string.IsNullOrWhiteSpace(request.PlanHandle))
        {
            return Results.BadRequest(new { message = "planHandle is required. List plans at GET /api/subscription-plans." });
        }

        var appUser = await userManager.FindByNameAsync(userName);
        if (appUser is null)
        {
            return Results.NotFound();
        }

        try
        {
            var email = string.IsNullOrWhiteSpace(appUser.Email) ? userName : appUser.Email;
            var result = await _subscriptionService.SubscribeAsync(userName, email, request.PlanHandle.Trim(), CancellationToken.None);

            response.Subscription = SubscriptionDtoMapper.FromSubscription(result.Subscription);
            response.AlreadySubscribed = result.AlreadySubscribed;

            return result.AlreadySubscribed
                ? Results.Ok(response)
                : Results.Created("/api/my-subscriptions", response);
        }
        catch (MaxioApiException ex)
        {
            return SubscriptionEndpointErrors.FromMaxioApi(ex);
        }
        catch (MaxioConfigurationException ex)
        {
            return SubscriptionEndpointErrors.FromConfiguration(ex);
        }
    }
}
