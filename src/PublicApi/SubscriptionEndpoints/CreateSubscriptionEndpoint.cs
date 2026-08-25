using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.Infrastructure.Identity;
using Microsoft.eShopWeb.PublicApi.Maxio;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// Subscribes the authenticated user to a plan (idempotent)
/// </summary>
public class CreateSubscriptionEndpoint : IEndpoint<IResult, CreateSubscriptionRequest, HttpContext>
{
    private readonly IMaxioSubscriptionService _subscriptionService;
    private readonly UserManager<ApplicationUser> _userManager;

    public CreateSubscriptionEndpoint(
        IMaxioSubscriptionService subscriptionService,
        UserManager<ApplicationUser> userManager)
    {
        _subscriptionService = subscriptionService;
        _userManager = userManager;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/subscriptions",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (CreateSubscriptionRequest request, HttpContext httpContext) =>
            {
                return await HandleAsync(request, httpContext);
            })
            .Produces<CreateSubscriptionResponse>()
            .WithTags("SubscriptionEndpoints");
    }

    public async Task<IResult> HandleAsync(CreateSubscriptionRequest request, HttpContext httpContext)
    {
        var response = new CreateSubscriptionResponse(request.CorrelationId());

        var username = httpContext.User.Identity?.Name;
        if (string.IsNullOrEmpty(username))
        {
            return Results.Unauthorized();
        }

        var user = await _userManager.FindByNameAsync(username);
        if (user is null)
        {
            return Results.Unauthorized();
        }

        var subscription = await _subscriptionService.SubscribeAsync(
            user.Id,
            user.Email ?? username,
            request.PlanHandle,
            httpContext.RequestAborted);

        response.Subscription = subscription;
        return Results.Ok(response);
    }
}
