using System;
using System.Collections.Generic;
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

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>Creates (or returns) the caller's Maxio subscription for a selected plan.</summary>
public sealed class SubscribeEndpoint : IEndpoint<IResult, SubscribeRequest, IMaxioBillingService, UserManager<ApplicationUser>>
{
    private readonly IHttpContextAccessor _httpContextAccessor;

    public SubscribeEndpoint(IHttpContextAccessor httpContextAccessor)
    {
        _httpContextAccessor = httpContextAccessor;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/subscriptions",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (SubscribeRequest request, IMaxioBillingService billing, UserManager<ApplicationUser> userManager) =>
            {
                return await HandleAsync(request, billing, userManager);
            })
            .Produces<SubscriptionDto>()
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status502BadGateway)
            .WithTags("SubscriptionEndpoints");
    }

    public async Task<IResult> HandleAsync(SubscribeRequest request, IMaxioBillingService billing, UserManager<ApplicationUser> userManager)
    {
        var principal = _httpContextAccessor.HttpContext?.User;
        var user = principal is null ? null : await GetUserAsync(principal, userManager);
        if (user is null)
        {
            return Results.Unauthorized();
        }

        if (string.IsNullOrWhiteSpace(request.PlanHandle))
        {
            return Results.ValidationProblem(new Dictionary<string, string[]> { ["planHandle"] = new[] { "A planHandle is required." } });
        }

        try
        {
            return Results.Ok(await billing.SubscribeAsync(user, request.PlanHandle, _httpContextAccessor.HttpContext?.RequestAborted ?? CancellationToken.None));
        }
        catch (ArgumentException exception)
        {
            return Results.ValidationProblem(new Dictionary<string, string[]> { ["planHandle"] = new[] { exception.Message } });
        }
        catch (MaxioApiException)
        {
            return Results.Problem("The billing service could not complete the subscription.", statusCode: StatusCodes.Status502BadGateway);
        }
    }

    internal static async Task<ApplicationUser?> GetUserAsync(ClaimsPrincipal principal, UserManager<ApplicationUser> userManager)
    {
        var userName = principal.FindFirstValue(ClaimTypes.Name);
        return string.IsNullOrWhiteSpace(userName) ? null : await userManager.FindByNameAsync(userName);
    }
}
