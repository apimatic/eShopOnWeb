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
using Microsoft.Extensions.Logging;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// Subscribes the logged-in shopper (identified from the JWT) to the requested plan. Idempotent:
/// an existing non-terminal subscription to the same plan is returned instead of creating a duplicate.
/// </summary>
public class CreateSubscriptionEndpoint : IEndpoint<IResult, CreateSubscriptionRequest>
{
    private readonly IMaxioSubscriptionService _subscriptionService;
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly IHttpContextAccessor _httpContextAccessor;
    private readonly ILogger<CreateSubscriptionEndpoint> _logger;

    public CreateSubscriptionEndpoint(IMaxioSubscriptionService subscriptionService,
        UserManager<ApplicationUser> userManager,
        IHttpContextAccessor httpContextAccessor,
        ILogger<CreateSubscriptionEndpoint> logger)
    {
        _subscriptionService = subscriptionService;
        _userManager = userManager;
        _httpContextAccessor = httpContextAccessor;
        _logger = logger;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/subscriptions",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async (CreateSubscriptionRequest request) => await HandleAsync(request))
            .Accepts<CreateSubscriptionRequest>("application/json")
            .Produces<CreateSubscriptionResponse>()
            .WithTags("SubscriptionEndpoints");
    }

    public async Task<IResult> HandleAsync(CreateSubscriptionRequest request)
    {
        var response = new CreateSubscriptionResponse();

        if (request is null || string.IsNullOrWhiteSpace(request.PlanHandle))
        {
            return Results.BadRequest(new { message = "PlanHandle is required." });
        }

        try
        {
            var shopper = await ResolveShopperAsync(request);
            if (shopper is null)
            {
                return Results.Unauthorized();
            }

            var enrollment = await _subscriptionService.SubscribeAsync(shopper, request.PlanHandle, CurrentCancellationToken());
            response.Subscription = enrollment.Subscription;

            return enrollment.CreatedNew
                ? Results.Created($"/api/subscriptions/{enrollment.Subscription.Id}", response)
                : Results.Ok(response);
        }
        catch (MaxioBillingException ex)
        {
            return SubscriptionEndpointResult.From(ex);
        }
        catch (System.Exception ex)
        {
            return SubscriptionEndpointResult.FromUnexpected(ex, _logger, "creating the subscription");
        }
    }

    private async Task<MaxioShopper?> ResolveShopperAsync(CreateSubscriptionRequest request)
    {
        string? userName = _httpContextAccessor.HttpContext?.User.Identity?.Name;
        if (string.IsNullOrWhiteSpace(userName))
        {
            return null;
        }

        var applicationUser = await _userManager.FindByNameAsync(userName);
        if (applicationUser is null)
        {
            return null;
        }

        string email = string.IsNullOrWhiteSpace(applicationUser.Email) ? applicationUser.UserName ?? string.Empty : applicationUser.Email;
        return new MaxioShopper(applicationUser.Id, email, request.FirstName, request.LastName);
    }

    private CancellationToken CurrentCancellationToken()
    {
        return _httpContextAccessor.HttpContext?.RequestAborted ?? CancellationToken.None;
    }
}
