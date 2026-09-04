using System;
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
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.Subscriptions;

public sealed class CreateSubscriptionEndpoint : IEndpoint<IResult, SubscribeRequest>
{
    private readonly SubscriptionService _subscriptionService;
    private readonly UserManager<ApplicationUser> _userManager;

    public CreateSubscriptionEndpoint(
        SubscriptionService subscriptionService,
        UserManager<ApplicationUser> userManager)
    {
        _subscriptionService = subscriptionService;
        _userManager = userManager;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/subscriptions", HandleRouteAsync)
            .RequireAuthorization(new AuthorizeAttribute { AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme })
            .Produces<SubscribeResponse>(StatusCodes.Status201Created)
            .Produces(StatusCodes.Status400BadRequest)
            .WithTags("SubscriptionEndpoints");
    }

    public Task<IResult> HandleAsync(SubscribeRequest request) =>
        Task.FromResult<IResult>(Results.Unauthorized());

    private async Task<IResult> HandleRouteAsync(
        SubscribeRequest request,
        HttpContext httpContext,
        CancellationToken cancellationToken)
    {
        var user = await GetCurrentUserAsync(httpContext.User);
        if (user is null)
        {
            return Results.Unauthorized();
        }

        if (string.IsNullOrWhiteSpace(request.ProductHandle) || request.ProductHandle.Length > 255)
        {
            return Results.BadRequest("ProductHandle is required.");
        }

        var subscription = await _subscriptionService.SubscribeAsync(user, request.ProductHandle.Trim(), cancellationToken);
        return Results.Created("api/my-subscriptions", new SubscribeResponse(request.CorrelationId())
        {
            Subscription = subscription
        });
    }

    private async Task<ApplicationUser?> GetCurrentUserAsync(ClaimsPrincipal principal)
    {
        var userName = principal.FindFirstValue(ClaimTypes.Name);
        return string.IsNullOrWhiteSpace(userName)
            ? null
            : await _userManager.FindByNameAsync(userName);
    }
}
