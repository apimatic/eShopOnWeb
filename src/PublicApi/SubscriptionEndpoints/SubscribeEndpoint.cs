using System;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.Infrastructure.Identity;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public sealed class SubscribeEndpoint : IEndpoint<IResult, SubscribeRequest, UserManager<ApplicationUser>, ISubscriptionService>
{
    private readonly IHttpContextAccessor _httpContextAccessor;

    public SubscribeEndpoint(IHttpContextAccessor httpContextAccessor)
    {
        _httpContextAccessor = httpContextAccessor;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/subscriptions", HandleAsync)
            .RequireAuthorization(new AuthorizeAttribute { AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme })
            .Produces<SubscriptionResponse>(StatusCodes.Status201Created)
            .Produces<SubscriptionResponse>(StatusCodes.Status200OK)
            .WithTags("SubscriptionEndpoints");
    }

    public async Task<IResult> HandleAsync(
        SubscribeRequest request,
        UserManager<ApplicationUser> userManager,
        ISubscriptionService subscriptions)
    {
        var principal = _httpContextAccessor.HttpContext?.User;
        if (principal is null)
        {
            return Results.Unauthorized();
        }

        var username = principal.FindFirstValue(ClaimTypes.Name);
        if (string.IsNullOrWhiteSpace(username))
        {
            return Results.Unauthorized();
        }

        var user = await userManager.FindByNameAsync(username);
        if (user is null)
        {
            return Results.Unauthorized();
        }

        try
        {
            var result = await subscriptions.SubscribeAsync(user, request.ProductHandle, _httpContextAccessor.HttpContext?.RequestAborted ?? CancellationToken.None);
            var response = ToResponse(result.Subscription);
            return result.Created ? Results.Created("api/my-subscriptions", response) : Results.Ok(response);
        }
        catch (SubscriptionConflictException exception)
        {
            return Results.Conflict(new { error = exception.Message });
        }
        catch (MaxioConfigurationException exception)
        {
            return Results.Problem(exception.Message, statusCode: StatusCodes.Status500InternalServerError);
        }
        catch (MaxioApiException)
        {
            return Results.Problem("The subscription service is temporarily unavailable.", statusCode: StatusCodes.Status502BadGateway);
        }
    }

    internal static SubscriptionResponse ToResponse(SubscriptionDetails subscription) => new(
        subscription.Id,
        subscription.PlanHandle,
        subscription.PlanName,
        subscription.PriceInCents,
        subscription.State,
        subscription.NextBillingDate);
}
