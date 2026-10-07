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
using Microsoft.eShopWeb.PublicApi.MaxioBilling;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// Subscribes the authenticated shopper to a subscription plan. Idempotent:
/// a repeated or double-clicked request returns the existing subscription.
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
            (CreateSubscriptionRequest request, UserManager<ApplicationUser> userManager, IMaxioBillingService billingService) =>
            {
                return await HandleAsync(request, userManager, billingService);
            })
            .Produces<CreateSubscriptionResponse>()
            .WithTags("SubscriptionEndpoints");
    }

    public async Task<IResult> HandleAsync(CreateSubscriptionRequest request, UserManager<ApplicationUser> userManager, IMaxioBillingService billingService)
    {
        var httpContext = _httpContextAccessor.HttpContext;

        var user = await ResolveUserAsync(httpContext?.User ?? new ClaimsPrincipal(), userManager);
        if (user is null)
        {
            return Results.Unauthorized();
        }

        if (string.IsNullOrWhiteSpace(request.PlanHandle))
        {
            return Results.BadRequest(new { Message = "A planHandle is required." });
        }

        var subscription = await billingService.SubscribeAsync(user, request.PlanHandle.Trim(), httpContext?.RequestAborted ?? CancellationToken.None);

        var response = new CreateSubscriptionResponse { Subscription = subscription };
        return Results.Created($"api/my-subscriptions/{subscription.SubscriptionId}", response);
    }

    /// <summary>
    /// Resolves the eShopOnWeb user from the caller's token. The token carries
    /// the username claim; the identity store supplies the user id and email.
    /// </summary>
    internal static async Task<UserBillingIdentity?> ResolveUserAsync(ClaimsPrincipal principal, UserManager<ApplicationUser> userManager)
    {
        var username = principal.FindFirstValue(ClaimTypes.Name);
        if (string.IsNullOrWhiteSpace(username))
        {
            return null;
        }

        var user = await userManager.FindByNameAsync(username);
        if (user is null || string.IsNullOrWhiteSpace(user.Email))
        {
            return null;
        }

        var (firstName, lastName) = DeriveNames(user.UserName ?? user.Email);

        return new UserBillingIdentity(user.Id, user.Email, firstName, lastName);
    }

    /// <summary>
    /// Maxio requires a first and last name; the identity store only carries
    /// the username/email, so derive human-readable names from it.
    /// </summary>
    internal static (string FirstName, string LastName) DeriveNames(string userNameOrEmail)
    {
        var localPart = userNameOrEmail.Split('@')[0];
        var tokens = localPart.Split(new[] { '.', '_', '-' }, System.StringSplitOptions.RemoveEmptyEntries | System.StringSplitOptions.TrimEntries);

        var firstName = Capitalize(tokens.FirstOrDefault()) ?? "eShop";
        var lastName = tokens.Length > 1
            ? string.Join(" ", tokens.Skip(1).Select(t => Capitalize(t) ?? t))
            : "Customer";

        return (firstName, lastName);
    }

    private static string? Capitalize(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        return char.ToUpperInvariant(value[0]) + value[1..];
    }
}
