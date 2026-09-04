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
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.Subscriptions;

public sealed class ListMySubscriptionsEndpoint : IEndpoint<IResult>
{
    private readonly IHttpContextAccessor _httpContextAccessor;
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly ISubscriptionService _service;

    public ListMySubscriptionsEndpoint(IHttpContextAccessor httpContextAccessor,
        UserManager<ApplicationUser> userManager, ISubscriptionService service)
    {
        _httpContextAccessor = httpContextAccessor;
        _userManager = userManager;
        _service = service;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/my-subscriptions",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (CancellationToken cancellationToken) => await HandleRouteAsync(cancellationToken))
            .Produces<ListMySubscriptionsResponse>()
            .WithTags("SubscriptionEndpoints");
    }

    public Task<IResult> HandleAsync() => HandleRouteAsync(CancellationToken.None);

    private async Task<IResult> HandleRouteAsync(CancellationToken cancellationToken)
    {
        var context = _httpContextAccessor.HttpContext!;
        var userName = context.User.Identity?.Name;
        var user = string.IsNullOrWhiteSpace(userName) ? null : await _userManager.FindByNameAsync(userName);
        if (user is null)
            return Results.Unauthorized();

        try
        {
            var response = new ListMySubscriptionsResponse();
            response.Subscriptions.AddRange(await _service.ListMySubscriptionsAsync(user, cancellationToken));
            return Results.Ok(response);
        }
        catch (MaxioApiException)
        {
            return Results.Problem("The subscription billing service is unavailable.", statusCode: StatusCodes.Status502BadGateway);
        }
    }
}
