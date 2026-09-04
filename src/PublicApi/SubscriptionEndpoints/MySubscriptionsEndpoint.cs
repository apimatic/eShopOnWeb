using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Constants;
using Microsoft.eShopWeb.Infrastructure.Identity;
using Microsoft.eShopWeb.PublicApi.Maxio;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public sealed class MySubscriptionsEndpoint : IEndpoint<IResult, SubscriptionService>
{
    private readonly SubscriptionService _service;
    private readonly UserManager<ApplicationUser> _userManager;

    public MySubscriptionsEndpoint(SubscriptionService service, UserManager<ApplicationUser> userManager)
    {
        _service = service;
        _userManager = userManager;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/my-subscriptions", HandleRoute)
            .RequireAuthorization(AuthorizationConstants.PUBLIC_API_JWT_POLICY)
            .Produces<MySubscriptionsResponse>()
            .WithTags("SubscriptionEndpoints");
    }

    private async Task<IResult> HandleRoute(ClaimsPrincipal principal, CancellationToken cancellationToken)
    {
        var name = principal.FindFirstValue(ClaimTypes.Name);
        if (string.IsNullOrWhiteSpace(name))
        {
            return Results.Unauthorized();
        }

        var user = await _userManager.FindByNameAsync(name);
        if (user is null)
        {
            return Results.Unauthorized();
        }

        try
        {
            return Results.Ok(new MySubscriptionsResponse
            {
                Subscriptions = await _service.GetMySubscriptionsAsync(user, cancellationToken)
            });
        }
        catch (MaxioApiException ex)
        {
            return Results.Problem(ex.Message, statusCode: ex.StatusCode == 503 ? 503 : 502);
        }
    }

    public Task<IResult> HandleAsync(SubscriptionService service) =>
        HandleRoute(new ClaimsPrincipal(), CancellationToken.None);
}
