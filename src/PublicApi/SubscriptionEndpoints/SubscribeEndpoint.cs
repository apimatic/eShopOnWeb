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
/// Enrolls the calling user in a subscription plan, ensuring a Maxio customer exists for them first.
/// </summary>
public class SubscribeEndpoint : IEndpoint<IResult, SubscribeRequest, HttpContext>
{
    private readonly IMaxioSubscriptionService _subscriptionService;
    private readonly UserManager<ApplicationUser> _userManager;

    public SubscribeEndpoint(IMaxioSubscriptionService subscriptionService, UserManager<ApplicationUser> userManager)
    {
        _subscriptionService = subscriptionService;
        _userManager = userManager;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/subscriptions",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (SubscribeRequest request, HttpContext httpContext) =>
            {
                return await HandleAsync(request, httpContext);
            })
            .Produces<SubscribeResponse>()
            .WithTags("SubscriptionEndpoints");
    }

    public async Task<IResult> HandleAsync(SubscribeRequest request, HttpContext httpContext)
    {
        var response = new SubscribeResponse(request.CorrelationId());

        if (string.IsNullOrWhiteSpace(request.PlanHandle))
            return Results.BadRequest("planHandle is required.");

        var userName = httpContext.User.Identity?.Name;
        if (string.IsNullOrEmpty(userName))
            return Results.Unauthorized();

        var user = await _userManager.FindByNameAsync(userName);
        if (user is null || string.IsNullOrEmpty(user.Email))
            return Results.Unauthorized();

        var subscription = await _subscriptionService.SubscribeAsync(
            user.Id, user.Email, request.PlanHandle, httpContext.RequestAborted);

        response.Subscription = new SubscriptionDto
        {
            Id = subscription.Id,
            PlanHandle = subscription.PlanHandle,
            PlanName = subscription.PlanName,
            Price = subscription.PriceInCents / 100m,
            State = subscription.State,
            NextBillingDate = subscription.NextBillingDate
        };

        return Results.Ok(response);
    }
}
