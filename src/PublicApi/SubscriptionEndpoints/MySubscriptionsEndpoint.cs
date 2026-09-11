using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.Infrastructure.Identity;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class MySubscriptionsEndpoint : IEndpoint<IResult, EmptyRequest>
{
    private readonly IMaxioService _maxio;
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly IHttpContextAccessor _httpContextAccessor;

    public MySubscriptionsEndpoint(IMaxioService maxio, UserManager<ApplicationUser> userManager, IHttpContextAccessor httpContextAccessor)
    {
        _maxio = maxio;
        _userManager = userManager;
        _httpContextAccessor = httpContextAccessor;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/my-subscriptions",
            async () => await HandleAsync(new EmptyRequest()))
            .Produces<List<MySubscription>>()
            .WithTags("SubscriptionEndpoints");
    }

    public async Task<IResult> HandleAsync(EmptyRequest request)
    {
        var userName = _httpContextAccessor.HttpContext?.User.FindFirst(System.Security.Claims.ClaimTypes.Name)?.Value;
        if (string.IsNullOrEmpty(userName))
            return Results.Unauthorized();

        var user = await _userManager.FindByNameAsync(userName);
        if (user == null)
            return Results.NotFound(new { error = "User not found" });

        var subs = await _maxio.GetMySubscriptionsAsync(reference: user.Id);
        return Results.Ok(subs);
    }
}
