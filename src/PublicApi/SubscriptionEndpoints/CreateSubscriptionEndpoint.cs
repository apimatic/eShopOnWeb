using System;
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

public sealed class CreateSubscriptionEndpoint : IEndpoint<IResult, SubscribeRequest, SubscriptionService>
{
    private readonly SubscriptionService _service;
    private readonly UserManager<ApplicationUser> _userManager;

    public CreateSubscriptionEndpoint(SubscriptionService service, UserManager<ApplicationUser> userManager)
    {
        _service = service;
        _userManager = userManager;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/subscriptions", HandleRoute)
            .RequireAuthorization(AuthorizationConstants.PUBLIC_API_JWT_POLICY)
            .Produces<SubscribeResponse>(StatusCodes.Status201Created)
            .WithTags("SubscriptionEndpoints");
    }

    private async Task<IResult> HandleRoute(
        SubscribeRequest request,
        ClaimsPrincipal principal,
        CancellationToken cancellationToken)
    {
        var user = await FindCurrentUserAsync(principal);
        if (user is null)
        {
            return Results.Unauthorized();
        }

        try
        {
            var subscription = await _service.SubscribeAsync(user, request.PlanHandle, cancellationToken);
            return Results.Created("api/my-subscriptions", new SubscribeResponse { Subscription = subscription });
        }
        catch (ArgumentException ex)
        {
            return Results.BadRequest(new { error = ex.Message });
        }
        catch (SubscriptionConflictException ex)
        {
            return Results.Conflict(new { error = ex.Message });
        }
        catch (MaxioApiException ex)
        {
            return Results.Problem(ex.Message, statusCode: ex.StatusCode == 503 ? 503 : 502);
        }
    }

    private Task<ApplicationUser?> FindCurrentUserAsync(ClaimsPrincipal principal)
    {
        var name = principal.FindFirstValue(ClaimTypes.Name);
        return string.IsNullOrWhiteSpace(name)
            ? Task.FromResult<ApplicationUser?>(null)
            : _userManager.FindByNameAsync(name);
    }

    public Task<IResult> HandleAsync(SubscribeRequest request, SubscriptionService service) =>
        HandleRoute(request, new ClaimsPrincipal(), CancellationToken.None);
}
