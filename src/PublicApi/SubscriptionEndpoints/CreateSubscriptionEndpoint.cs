using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.Infrastructure.Identity;
using Microsoft.eShopWeb.Infrastructure.Maxio;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// Subscribes the authenticated shopper to a plan. Idempotent: enrolling twice into the
/// same plan never creates a second active subscription or a duplicate Maxio customer.
/// </summary>
public class CreateSubscriptionEndpoint : IEndpoint<IResult, CreateSubscriptionRequest, UserManager<ApplicationUser>, IMaxioBillingService>
{
    private readonly IHttpContextAccessor _httpContextAccessor;

    public CreateSubscriptionEndpoint(IHttpContextAccessor httpContextAccessor)
    {
        _httpContextAccessor = httpContextAccessor;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/subscriptions",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (CreateSubscriptionRequest request, UserManager<ApplicationUser> userManager, IMaxioBillingService maxioBillingService) =>
            {
                return await HandleAsync(request, userManager, maxioBillingService);
            })
           .Produces<CreateSubscriptionResponse>()
           .WithTags("SubscriptionEndpoints");
    }

    public async Task<IResult> HandleAsync(
        CreateSubscriptionRequest request,
        UserManager<ApplicationUser> userManager,
        IMaxioBillingService maxioBillingService)
    {
        var userName = _httpContextAccessor.HttpContext?.User?.FindFirstValue(ClaimTypes.Name);
        if (string.IsNullOrEmpty(userName))
        {
            return Results.Unauthorized();
        }

        var user = await userManager.FindByNameAsync(userName);
        if (user == null)
        {
            return Results.Unauthorized();
        }

        if (string.IsNullOrWhiteSpace(request.PlanHandle))
        {
            return Results.BadRequest(new { Message = "planHandle is required." });
        }

        var plan = await maxioBillingService.GetPlanByHandleAsync(request.PlanHandle);
        if (plan == null)
        {
            return Results.NotFound(new { Message = $"Unknown subscription plan '{request.PlanHandle}'." });
        }

        var customerReference = $"eshopweb-{user.Id}";
        var (firstName, lastName) = SplitName(user);
        var result = await maxioBillingService.SubscribeAsync(new MaxioSubscribeRequest
        {
            CustomerReference = customerReference,
            CustomerEmail = user.Email ?? user.UserName ?? string.Empty,
            CustomerFirstName = firstName,
            CustomerLastName = lastName,
            PlanHandle = plan.Handle,
            SubscriptionReference = $"{customerReference}:{plan.Handle}"
        });

        return Results.Created($"api/my-subscriptions", MapResponse(result));
    }

    private static CreateSubscriptionResponse MapResponse(MaxioSubscribeResult result) => new()
    {
        AlreadySubscribed = result.AlreadySubscribed,
        Subscription = MapSummary(result.Subscription)
    };

    internal static SubscriptionSummaryDto MapSummary(MaxioSubscription subscription) => new()
    {
        SubscriptionId = subscription.Id,
        State = subscription.State,
        PlanHandle = subscription.PlanHandle ?? string.Empty,
        PlanName = subscription.PlanName,
        Price = (subscription.PriceInCents ?? 0) / 100m,
        Currency = subscription.Currency,
        Interval = subscription.Interval ?? 1,
        IntervalUnit = subscription.IntervalUnit,
        NextBillingDate = subscription.NextBillingDate,
        ActivatedAt = subscription.ActivatedAt,
        CanceledAt = subscription.CanceledAt
    };

    /// <summary>
    /// Maxio requires first/last names; derive deterministic ones from the eShopOnWeb identity.
    /// </summary>
    private static (string FirstName, string LastName) SplitName(ApplicationUser user)
    {
        var source = user.UserName ?? user.Email ?? "eShop";
        var localPart = source.Contains('@') ? source[..source.IndexOf('@')] : source;
        var tokens = localPart.Split(new[] { '.', '_', '-' }, System.StringSplitOptions.RemoveEmptyEntries);
        var firstName = tokens.Length > 0 ? tokens[0] : localPart;
        var lastName = tokens.Length > 1 ? string.Join(" ", tokens.Skip(1)) : "Customer";
        return (firstName, lastName);
    }
}
