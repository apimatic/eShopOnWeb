using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.Infrastructure.Identity;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class SubscribeRequest : BaseRequest
{
    public string ProductHandle { get; set; } = string.Empty;
}

public class SubscribeEndpoint : IEndpoint<IResult, SubscribeRequest>
{
    private readonly IMaxioService _maxio;
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly IHttpContextAccessor _httpContextAccessor;

    public SubscribeEndpoint(IMaxioService maxio, UserManager<ApplicationUser> userManager, IHttpContextAccessor httpContextAccessor)
    {
        _maxio = maxio;
        _userManager = userManager;
        _httpContextAccessor = httpContextAccessor;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/subscriptions",
            async (SubscribeRequest request) => await HandleAsync(request))
            .Produces<SubscriptionResult>()
            .WithTags("SubscriptionEndpoints");
    }

    public async Task<IResult> HandleAsync(SubscribeRequest request)
    {
        var userName = _httpContextAccessor.HttpContext?.User.FindFirst(System.Security.Claims.ClaimTypes.Name)?.Value;
        if (string.IsNullOrEmpty(userName))
            return Results.Unauthorized();

        var user = await _userManager.FindByNameAsync(userName);
        if (user == null)
            return Results.NotFound(new { error = "User not found" });

        var result = await _maxio.SubscribeAsync(
            reference: user.Id,
            email: user.Email ?? user.UserName,
            firstName: "User",
            lastName: "User",
            productHandle: request.ProductHandle);

        return Results.Ok(result);
    }
}
