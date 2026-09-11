using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.Infrastructure.Identity;
using Microsoft.eShopWeb.PublicApi.Services;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class MySubscriptionsEndpoint : IEndpoint<IResult, EmptyRequest, IMaxioService>
{
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly IHttpContextAccessor _httpContextAccessor;

    public MySubscriptionsEndpoint(UserManager<ApplicationUser> userManager, IHttpContextAccessor httpContextAccessor)
    {
        _userManager = userManager;
        _httpContextAccessor = httpContextAccessor;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/my-subscriptions",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async (IMaxioService maxio) =>
            {
                return await HandleAsync(new EmptyRequest(), maxio);
            })
            .Produces<object>()
            .WithTags("SubscriptionEndpoints");
    }

    public async Task<IResult> HandleAsync(EmptyRequest request, IMaxioService maxio)
    {
        var http = _httpContextAccessor.HttpContext;
        var userName = http?.User.Identity?.Name ?? "";
        if (string.IsNullOrEmpty(userName)) return Results.Unauthorized();
        var user = await _userManager.FindByNameAsync(userName);
        if (user == null) return Results.BadRequest(new { error = "User not found" });
        var reference = user.UserName ?? user.Email ?? user.Id;
        var subs = await maxio.GetCustomerSubscriptionsAsync(reference);
        return Results.Ok(subs ?? new System.Text.Json.Nodes.JsonArray());
    }
}
