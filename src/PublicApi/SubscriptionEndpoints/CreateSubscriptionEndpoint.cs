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
/// Subscribes the authenticated user to a plan. Idempotent: a repeated subscribe
/// for the same user and plan returns the existing subscription.
/// </summary>
public class CreateSubscriptionEndpoint : IEndpoint<IResult, CreateSubscriptionRequest, ISubscriptionService>
{
    private readonly IHttpContextAccessor _httpContextAccessor;
    private readonly UserManager<ApplicationUser> _userManager;

    public CreateSubscriptionEndpoint(IHttpContextAccessor httpContextAccessor, UserManager<ApplicationUser> userManager)
    {
        _httpContextAccessor = httpContextAccessor;
        _userManager = userManager;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/subscriptions",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (CreateSubscriptionRequest request, ISubscriptionService subscriptionService) =>
            {
                return await HandleAsync(request, subscriptionService);
            })
            .Produces<CreateSubscriptionResponse>()
            .WithTags("SubscriptionEndpoints");
    }

    public async Task<IResult> HandleAsync(CreateSubscriptionRequest request, ISubscriptionService subscriptionService)
    {
        if (string.IsNullOrWhiteSpace(request.ProductHandle))
        {
            return Results.BadRequest("ProductHandle is required.");
        }

        var user = await GetCurrentUserAsync();
        if (user is null)
        {
            return Results.Unauthorized();
        }

        var subscription = await subscriptionService.SubscribeAsync(new SubscribeCommand
        {
            UserId = user.Id,
            Email = user.Email ?? user.UserName ?? string.Empty,
            FirstName = FirstNameFrom(user),
            LastName = "eShopOnWeb Shopper",
            ProductHandle = request.ProductHandle
        });

        var response = new CreateSubscriptionResponse(request.CorrelationId())
        {
            Subscription = subscription
        };
        return Results.Ok(response);
    }

    private async Task<ApplicationUser?> GetCurrentUserAsync()
    {
        var username = _httpContextAccessor.HttpContext?.User.Identity?.Name;
        if (string.IsNullOrEmpty(username))
        {
            return null;
        }
        return await _userManager.FindByNameAsync(username);
    }

    private static string FirstNameFrom(ApplicationUser user)
    {
        var name = user.UserName ?? "Shopper";
        var atIndex = name.IndexOf('@');
        return atIndex > 0 ? name.Substring(0, atIndex) : name;
    }
}
