using System.Security.Claims;
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

public sealed class MySubscriptionsEndpoint : IEndpoint<IResult, IMaxioBillingService>
{
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly IHttpContextAccessor _httpContextAccessor;

    public MySubscriptionsEndpoint(
        UserManager<ApplicationUser> userManager,
        IHttpContextAccessor httpContextAccessor)
    {
        _userManager = userManager;
        _httpContextAccessor = httpContextAccessor;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/my-subscriptions", async (
                ClaimsPrincipal principal,
                UserManager<ApplicationUser> userManager,
                IMaxioBillingService billingService,
                CancellationToken cancellationToken) =>
            await HandleAsync(principal, userManager, billingService, cancellationToken))
            .RequireAuthorization(new AuthorizeAttribute { AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme })
            .Produces<MySubscriptionsResponse>()
            .WithTags("SubscriptionEndpoints");
    }

    public async Task<IResult> HandleAsync(
        ClaimsPrincipal principal,
        UserManager<ApplicationUser> userManager,
        IMaxioBillingService billingService,
        CancellationToken cancellationToken)
    {
        var username = principal.Identity?.Name;
        if (string.IsNullOrWhiteSpace(username))
            return Results.Unauthorized();

        var user = await userManager.FindByNameAsync(username);
        if (user is null)
            return Results.Unauthorized();

        var subscriptions = await billingService.GetMySubscriptionsAsync(user, cancellationToken);
        return Results.Ok(new MySubscriptionsResponse { Subscriptions = subscriptions });
    }

    public Task<IResult> HandleAsync(IMaxioBillingService billingService) =>
        HandleAsync(
            _httpContextAccessor.HttpContext?.User ?? new ClaimsPrincipal(),
            _userManager,
            billingService,
            CancellationToken.None);
}
