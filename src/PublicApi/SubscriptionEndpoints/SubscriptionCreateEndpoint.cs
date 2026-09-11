using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.Infrastructure.Identity;
using Microsoft.eShopWeb.PublicApi.Enrollment;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class SubscriptionCreateRequest
{
    public string ProductHandle { get; set; } = string.Empty;
}

public class SubscriptionCreateResponse
{
    public int SubscriptionId { get; set; }
    public string State { get; set; } = string.Empty;
    public string ProductName { get; set; } = string.Empty;
    public string ProductHandle { get; set; } = string.Empty;
    public int PriceInCents { get; set; }
    public string NextBillingDate { get; set; } = string.Empty;
}

public class SubscriptionCreateEndpoint : IEndpoint<IResult, IMaxioService>
{
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly IHttpContextAccessor _http;

    public SubscriptionCreateEndpoint(UserManager<ApplicationUser> userManager, IHttpContextAccessor http)
    {
        _userManager = userManager;
        _http = http;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/subscriptions",
            async (SubscriptionCreateRequest req, IMaxioService maxio, ClaimsPrincipal user) =>
            {
                return await ExecuteAsync(maxio, req, user);
            })
           .Produces<SubscriptionCreateResponse>()
           .WithTags("SubscriptionEndpoints")
           .RequireAuthorization();
    }

    public async Task<IResult> HandleAsync(IMaxioService maxio)
    {
        // Not used directly; lambda delegates to ExecuteAsync
        return await ExecuteAsync(maxio, new SubscriptionCreateRequest(), _http.HttpContext?.User ?? new ClaimsPrincipal());
    }

    private async Task<IResult> ExecuteAsync(IMaxioService maxio, SubscriptionCreateRequest req, ClaimsPrincipal user)
    {
        var userName = user.FindFirst(ClaimTypes.Name)?.Value ?? user.Identity?.Name ?? string.Empty;
        if (string.IsNullOrEmpty(userName)) return Results.Unauthorized();

        var appUser = await _userManager.FindByNameAsync(userName);
        if (appUser == null) return Results.Unauthorized();

        var email = appUser.Email ?? userName;
        var reference = userName;

        // Idempotent customer creation / lookup
        var existing = await maxio.FindCustomerByReferenceAsync(reference);
        int customerId;
        if (existing == null)
        {
            var created = await maxio.CreateCustomerAsync(
                "User",
                "User",
                email,
                reference);
            customerId = created?["customer"]?["id"]?.GetValue<int>() ?? 0;
        }
        else
        {
            customerId = existing["customer"]?["id"]?.GetValue<int>() ?? 0;
        }

        if (string.IsNullOrWhiteSpace(req.ProductHandle)) req.ProductHandle = "eshop-pro";

        var subResp = await maxio.CreateSubscriptionAsync(req.ProductHandle, reference);
        if (subResp == null) return Results.BadRequest("Subscription creation failed");

        var sub = subResp["subscription"];
        return Results.Ok(new SubscriptionCreateResponse
        {
            SubscriptionId = sub?["id"]?.GetValue<int>() ?? 0,
            State = sub?["state"]?.GetValue<string>() ?? string.Empty,
            ProductName = sub?["product"]?["name"]?.GetValue<string>() ?? string.Empty,
            ProductHandle = sub?["product"]?["handle"]?.GetValue<string>() ?? string.Empty,
            PriceInCents = sub?["product_price_in_cents"]?.GetValue<int>() ?? 0,
            NextBillingDate = sub?["current_period_ends_at"]?.GetValue<string>() ?? string.Empty
        });
    }
}
