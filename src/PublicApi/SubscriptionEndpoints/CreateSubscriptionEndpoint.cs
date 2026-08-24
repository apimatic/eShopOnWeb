using System.Linq;
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
using Microsoft.Extensions.Options;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// Subscribes the authenticated user to a plan (idempotent)
/// </summary>
public class CreateSubscriptionEndpoint : IEndpoint<IResult, CreateSubscriptionRequest, ClaimsPrincipal>
{
    private readonly IMaxioClient _maxioClient;
    private readonly MaxioBillingService _billingService;
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly MaxioSettings _maxioSettings;

    public CreateSubscriptionEndpoint(
        IMaxioClient maxioClient,
        MaxioBillingService billingService,
        UserManager<ApplicationUser> userManager,
        IOptions<MaxioSettings> maxioSettings)
    {
        _maxioClient = maxioClient;
        _billingService = billingService;
        _userManager = userManager;
        _maxioSettings = maxioSettings.Value;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/subscriptions",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (CreateSubscriptionRequest request, ClaimsPrincipal claimsPrincipal, CancellationToken cancellationToken) =>
            {
                return await HandleAsync(request, claimsPrincipal, cancellationToken);
            })
            .Produces<CreateSubscriptionResponse>()
            .WithTags("SubscriptionEndpoints");
    }

    public Task<IResult> HandleAsync(CreateSubscriptionRequest request, ClaimsPrincipal claimsPrincipal)
    {
        return HandleAsync(request, claimsPrincipal, CancellationToken.None);
    }

    public async Task<IResult> HandleAsync(CreateSubscriptionRequest request, ClaimsPrincipal claimsPrincipal, CancellationToken cancellationToken)
    {
        var response = new CreateSubscriptionResponse(request.CorrelationId());

        if (string.IsNullOrWhiteSpace(request.ProductHandle))
        {
            return Results.BadRequest("ProductHandle is required.");
        }

        var user = await GetCurrentUserAsync(claimsPrincipal);
        if (user is null)
        {
            return Results.Unauthorized();
        }

        // The plan must be one of the active plans in the configured product family.
        var products = await _maxioClient.ListProductsAsync(cancellationToken);
        var plan = products.FirstOrDefault(p =>
            p.ArchivedAt is null &&
            string.Equals(p.Handle, request.ProductHandle, System.StringComparison.OrdinalIgnoreCase) &&
            string.Equals(p.ProductFamily?.Handle, _maxioSettings.ProductFamilyHandle, System.StringComparison.OrdinalIgnoreCase));
        if (plan is null)
        {
            return Results.NotFound($"No active subscription plan with handle '{request.ProductHandle}' was found.");
        }

        var customer = await _billingService.EnsureCustomerAsync(user.Id, user.Email ?? user.UserName!, cancellationToken);
        var (subscription, alreadyExisted) = await _billingService.SubscribeAsync(customer, plan.Handle!, cancellationToken);

        response.Subscription = Map(subscription);
        response.AlreadyExisted = alreadyExisted;
        return alreadyExisted ? Results.Ok(response) : Results.Created($"api/my-subscriptions", response);
    }

    internal static SubscriptionDto Map(MaxioSubscription subscription) => new()
    {
        Id = subscription.Id,
        State = subscription.State,
        ProductHandle = subscription.Product?.Handle ?? string.Empty,
        ProductName = subscription.Product?.Name ?? string.Empty,
        PriceInCents = subscription.ProductPriceInCents,
        NextBillingDate = subscription.CurrentPeriodEndsAt ?? subscription.NextAssessmentAt,
        ActivatedAt = subscription.ActivatedAt,
        CreatedAt = subscription.CreatedAt
    };

    private async Task<ApplicationUser?> GetCurrentUserAsync(ClaimsPrincipal claimsPrincipal)
    {
        var username = claimsPrincipal.FindFirst(ClaimTypes.Name)?.Value;
        if (string.IsNullOrEmpty(username))
        {
            return null;
        }

        return await _userManager.FindByNameAsync(username);
    }
}
