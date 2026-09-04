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

public sealed class CreateSubscriptionEndpoint : IEndpoint<IResult, CreateSubscriptionRequest, IMaxioBillingService>
{
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly IHttpContextAccessor _httpContextAccessor;

    public CreateSubscriptionEndpoint(
        UserManager<ApplicationUser> userManager,
        IHttpContextAccessor httpContextAccessor)
    {
        _userManager = userManager;
        _httpContextAccessor = httpContextAccessor;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/subscriptions", async (
                CreateSubscriptionRequest request,
                ClaimsPrincipal principal,
                UserManager<ApplicationUser> userManager,
                IMaxioBillingService billingService,
                CancellationToken cancellationToken) =>
            await HandleAsync(request, principal, userManager, billingService, cancellationToken))
            .RequireAuthorization(new AuthorizeAttribute { AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme })
            .Produces<SubscriptionDto>(StatusCodes.Status201Created)
            .ProducesValidationProblem()
            .WithTags("SubscriptionEndpoints");
    }

    public async Task<IResult> HandleAsync(
        CreateSubscriptionRequest request,
        ClaimsPrincipal principal,
        UserManager<ApplicationUser> userManager,
        IMaxioBillingService billingService,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.PlanHandle))
            return Results.ValidationProblem(new Dictionary<string, string[]> { [nameof(request.PlanHandle)] = new[] { "A plan handle is required." } });

        var user = await FindUserAsync(principal, userManager);
        if (user is null)
            return Results.Unauthorized();

        try
        {
            var subscription = await billingService.SubscribeAsync(user, request.PlanHandle, cancellationToken);
            return Results.Created("api/my-subscriptions", subscription);
        }
        catch (SubscriptionPlanNotFoundException ex)
        {
            return Results.NotFound(new { error = ex.Message });
        }
    }

    public Task<IResult> HandleAsync(CreateSubscriptionRequest request, IMaxioBillingService billingService) =>
        HandleAsync(
            request,
            _httpContextAccessor.HttpContext?.User ?? new ClaimsPrincipal(),
            _userManager,
            billingService,
            CancellationToken.None);

    private static Task<ApplicationUser?> FindUserAsync(ClaimsPrincipal principal, UserManager<ApplicationUser> userManager)
    {
        var username = principal.Identity?.Name;
        return string.IsNullOrWhiteSpace(username)
            ? Task.FromResult<ApplicationUser?>(null)
            : userManager.FindByNameAsync(username);
    }
}
