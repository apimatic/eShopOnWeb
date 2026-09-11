using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.Infrastructure.Identity;
using Microsoft.eShopWeb.PublicApi.Services;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class CreateSubscriptionEndpoint : IEndpoint<IResult, CreateSubscriptionRequest, IMaxioService>
{
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly IHttpContextAccessor _httpContextAccessor;

    public CreateSubscriptionEndpoint(UserManager<ApplicationUser> userManager, IHttpContextAccessor httpContextAccessor)
    {
        _userManager = userManager;
        _httpContextAccessor = httpContextAccessor;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/subscriptions",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async (CreateSubscriptionRequest request, IMaxioService maxio) =>
            {
                return await HandleAsync(request, maxio);
            })
            .Produces<object>()
            .WithTags("SubscriptionEndpoints");
    }

    public async Task<IResult> HandleAsync(CreateSubscriptionRequest request, IMaxioService maxio)
    {
        var http = _httpContextAccessor.HttpContext;
        var userName = http?.User.Identity?.Name ?? "";
        if (string.IsNullOrEmpty(userName)) return Results.Unauthorized();
        var user = await _userManager.FindByNameAsync(userName);
        if (user == null) return Results.BadRequest(new { error = "User not found" });
        var reference = user.UserName ?? user.Email ?? user.Id;
        var email = user.Email ?? "";
        var customer = await maxio.FindCustomerByReferenceAsync(reference);
        if (customer == null) customer = await maxio.CreateCustomerAsync(reference, email, userName, "");
        if (customer == null) return Results.BadRequest(new { error = "Failed to create/find customer" });
        var subs = await maxio.GetCustomerSubscriptionsAsync(reference);
        if (subs != null)
        {
            var arr = subs.AsArray();
            if (arr != null)
            {
                foreach (var s in arr)
                {
                    var prod = s?["product"];
                    if (prod != null && prod["handle"]?.GetValue<string>() == request.ProductHandle)
                    {
                        return Results.Ok(new { subscriptionId = s?["id"]?.GetValue<int>() ?? 0, state = s?["state"]?.GetValue<string>(), productHandle = request.ProductHandle, nextBillingDate = s?["next_assessment_at"]?.GetValue<string>() ?? s?["current_period_ends_at"]?.GetValue<string>(), customerReference = reference });
                    }
                }
            }
        }
        var sub = await maxio.CreateSubscriptionAsync(reference, request.ProductHandle);
        if (sub == null) return Results.BadRequest(new { error = "Failed to create subscription" });
        var subObj = sub["subscription"] ?? sub;
        return Results.Ok(new { subscriptionId = subObj?["id"]?.GetValue<int>() ?? 0, state = subObj?["state"]?.GetValue<string>(), productHandle = request.ProductHandle, nextBillingDate = subObj?["next_assessment_at"]?.GetValue<string>() ?? subObj?["current_period_ends_at"]?.GetValue<string>(), customerReference = reference });
    }
}
