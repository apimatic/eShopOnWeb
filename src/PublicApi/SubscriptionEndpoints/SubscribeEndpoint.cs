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
using Microsoft.eShopWeb.PublicApi.Maxio;
using Microsoft.eShopWeb.PublicApi.SubscriptionPlanEndpoints;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// Subscribes the authenticated user to a plan. Idempotent: repeating the call
/// returns the existing subscription rather than creating a duplicate.
/// </summary>
public class SubscribeEndpoint : IEndpoint<IResult, SubscribeRequest, ISubscriptionBillingService>
{
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly IHttpContextAccessor _httpContextAccessor;

    public SubscribeEndpoint(UserManager<ApplicationUser> userManager, IHttpContextAccessor httpContextAccessor)
    {
        _userManager = userManager;
        _httpContextAccessor = httpContextAccessor;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/subscriptions",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (SubscribeRequest request, ISubscriptionBillingService billingService, CancellationToken cancellationToken) =>
            {
                return await HandleAsync(request, billingService, cancellationToken);
            })
            .Produces<SubscribeResponse>()
            .WithTags("SubscriptionEndpoints");
    }

    public Task<IResult> HandleAsync(SubscribeRequest request, ISubscriptionBillingService billingService)
        => HandleAsync(request, billingService, CancellationToken.None);

    public async Task<IResult> HandleAsync(SubscribeRequest request, ISubscriptionBillingService billingService,
        CancellationToken cancellationToken)
    {
        var user = await ResolveBillingUserAsync(_userManager, _httpContextAccessor.HttpContext?.User);
        if (user is null)
        {
            return Results.Unauthorized();
        }

        try
        {
            var subscription = await billingService.SubscribeAsync(user, request.ProductHandle, cancellationToken);
            var response = new SubscribeResponse(request.CorrelationId())
            {
                Subscription = subscription
            };
            return Results.Ok(response);
        }
        catch (MaxioBillingException ex)
        {
            return ListSubscriptionPlansEndpoint.ToProblem(ex);
        }
    }

    internal static async Task<BillingUser?> ResolveBillingUserAsync(UserManager<ApplicationUser> userManager, ClaimsPrincipal? caller)
    {
        var username = caller?.FindFirst(ClaimTypes.Name)?.Value;
        if (string.IsNullOrEmpty(username))
        {
            return null;
        }

        var identityUser = await userManager.FindByNameAsync(username);
        if (identityUser is null)
        {
            return null;
        }

        var localPart = (identityUser.Email ?? username).Split('@')[0];
        return new BillingUser(identityUser.Id, identityUser.Email ?? username, localPart, localPart);
    }
}
