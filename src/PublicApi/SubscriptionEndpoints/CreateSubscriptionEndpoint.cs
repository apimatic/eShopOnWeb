using System.Security.Claims;
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
/// Subscribe the authenticated shopper to a Maxio plan
/// </summary>
public class CreateSubscriptionEndpoint : IEndpoint<IResult, CreateSubscriptionRequest, ISubscriptionBillingService>
{
    private readonly IHttpContextAccessor _httpContextAccessor;
    private readonly UserManager<ApplicationUser> _userManager;

    public CreateSubscriptionEndpoint(
        IHttpContextAccessor httpContextAccessor,
        UserManager<ApplicationUser> userManager)
    {
        _httpContextAccessor = httpContextAccessor;
        _userManager = userManager;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/subscriptions",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
            async (CreateSubscriptionRequest request, ISubscriptionBillingService billingService) =>
            {
                return await HandleAsync(request, billingService);
            })
            .Produces<CreateSubscriptionResponse>(StatusCodes.Status201Created)
            .Produces<CreateSubscriptionResponse>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status400BadRequest)
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status404NotFound)
            .WithTags("SubscriptionEndpoints");
    }

    public async Task<IResult> HandleAsync(CreateSubscriptionRequest request, ISubscriptionBillingService billingService)
    {
        if (string.IsNullOrWhiteSpace(request.ProductHandle))
        {
            return Results.BadRequest("productHandle is required.");
        }

        var user = await ResolveCurrentUserAsync();
        if (user is null)
        {
            return Results.Unauthorized();
        }

        var result = await billingService.SubscribeAsync(
            user.Id,
            user.Email ?? user.UserName ?? string.Empty,
            user.UserName ?? user.Email ?? user.Id,
            request.ProductHandle);

        var response = new CreateSubscriptionResponse(request.CorrelationId())
        {
            Subscription = SubscriptionMapping.ToDto(result.Subscription)
        };

        return result.Created
            ? Results.Created("api/my-subscriptions", response)
            : Results.Ok(response);
    }

    private async Task<ApplicationUser?> ResolveCurrentUserAsync()
    {
        var identityName = _httpContextAccessor.HttpContext?.User.Identity?.Name
            ?? _httpContextAccessor.HttpContext?.User.FindFirstValue(ClaimTypes.Name);

        if (string.IsNullOrWhiteSpace(identityName))
        {
            return null;
        }

        return await _userManager.FindByNameAsync(identityName);
    }
}
