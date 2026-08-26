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
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// Subscribes the authenticated user to a plan (idempotent: an existing live
/// subscription for the plan is returned instead of creating a duplicate)
/// </summary>
public class CreateSubscriptionEndpoint : IEndpoint<IResult, SubscribeRequest, IMaxioSubscriptionService>
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
            (SubscribeRequest request, IMaxioSubscriptionService subscriptionService) =>
            {
                return await HandleAsync(request, subscriptionService);
            })
            .Produces<SubscribeResponse>()
            .WithTags("SubscriptionEndpoints");
    }

    public async Task<IResult> HandleAsync(SubscribeRequest request, IMaxioSubscriptionService subscriptionService)
    {
        if (string.IsNullOrWhiteSpace(request.ProductHandle))
        {
            return Results.BadRequest("ProductHandle is required.");
        }

        var userContext = await SubscriptionUserContextResolver.ResolveAsync(_httpContextAccessor, _userManager);
        if (userContext is null)
        {
            return Results.Unauthorized();
        }

        var ct = _httpContextAccessor.HttpContext?.RequestAborted ?? CancellationToken.None;
        var response = new SubscribeResponse(request.CorrelationId())
        {
            Subscription = await subscriptionService.SubscribeAsync(userContext, request.ProductHandle, ct)
        };
        return Results.Ok(response);
    }
}
