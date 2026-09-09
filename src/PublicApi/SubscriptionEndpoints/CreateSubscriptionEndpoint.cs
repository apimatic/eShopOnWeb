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
/// Subscribes the authenticated user to a plan (Maxio product). Idempotent: a
/// repeated subscribe for the same user and plan returns the existing subscription.
/// </summary>
public class CreateSubscriptionEndpoint : IEndpoint<IResult, CreateSubscriptionRequest>
{
    private readonly ISubscriptionService _subscriptionService;
    private readonly IHttpContextAccessor _httpContextAccessor;
    private readonly UserManager<ApplicationUser> _userManager;

    public CreateSubscriptionEndpoint(
        ISubscriptionService subscriptionService,
        IHttpContextAccessor httpContextAccessor,
        UserManager<ApplicationUser> userManager)
    {
        _subscriptionService = subscriptionService;
        _httpContextAccessor = httpContextAccessor;
        _userManager = userManager;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/subscriptions",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (CreateSubscriptionRequest request) =>
            {
                return await HandleAsync(request);
            })
            .Produces<CreateSubscriptionResponse>()
            .WithTags("SubscriptionEndpoints");
    }

    public async Task<IResult> HandleAsync(CreateSubscriptionRequest request)
    {
        var user = await AuthenticatedUser.GetAsync(_httpContextAccessor, _userManager);
        if (user is null)
        {
            return Results.Unauthorized();
        }

        if (string.IsNullOrWhiteSpace(request.PlanHandle))
        {
            return Results.BadRequest(new { Message = "planHandle is required." });
        }

        var subscription = await _subscriptionService.SubscribeAsync(
            user.Id,
            user.UserName ?? string.Empty,
            user.Email ?? string.Empty,
            request.PlanHandle.Trim());

        return Results.Created("api/my-subscriptions", new CreateSubscriptionResponse
        {
            Subscription = new SubscriptionDto
            {
                Id = subscription.Id,
                Reference = subscription.Reference,
                PlanHandle = subscription.PlanHandle,
                PlanName = subscription.PlanName,
                PriceInCents = subscription.PriceInCents,
                State = subscription.State,
                CustomerId = subscription.CustomerId,
                ActivatedAt = subscription.ActivatedAt,
                NextBillingAt = subscription.NextBillingAt,
                CanceledAt = subscription.CanceledAt,
                CancelAtEndOfPeriod = subscription.CancelAtEndOfPeriod
            }
        });
    }
}
