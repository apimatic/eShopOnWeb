using System;
using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Models.Subscription;
using Microsoft.eShopWeb.Infrastructure.Billing.Maxio;
using Microsoft.eShopWeb.Infrastructure.Identity;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// Subscribe the authenticated user (from the JWT) to a subscription plan.
/// Ensures a Maxio customer exists for the user, then enrolls them; idempotent on repeat calls.
/// </summary>
public class SubscriptionCreateEndpoint : IEndpoint<IResult, SubscriptionCreateRequest, ISubscriptionService>
{
    private readonly IHttpContextAccessor _httpContextAccessor;
    private readonly UserManager<ApplicationUser> _userManager;

    public SubscriptionCreateEndpoint(IHttpContextAccessor httpContextAccessor, UserManager<ApplicationUser> userManager)
    {
        _httpContextAccessor = httpContextAccessor;
        _userManager = userManager;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/subscriptions",
            async (SubscriptionCreateRequest request, ISubscriptionService subscriptionService) =>
            {
                return await HandleAsync(request, subscriptionService);
            })
            .RequireAuthorization()
            .Produces<SubscriptionCreateResponse>()
            .WithTags("SubscriptionEndpoints");
    }

    public async Task<IResult> HandleAsync(SubscriptionCreateRequest request, ISubscriptionService subscriptionService)
    {
        var user = _httpContextAccessor.HttpContext?.User;
        var username = user?.Identity?.Name
            ?? user?.FindFirstValue(ClaimTypes.Name)
            ?? user?.FindFirstValue("name");

        if (string.IsNullOrWhiteSpace(username))
        {
            return Results.Unauthorized();
        }

        var applicationUser = await _userManager.FindByNameAsync(username);
        if (applicationUser is null)
        {
            return Results.Unauthorized();
        }

        if (string.IsNullOrWhiteSpace(request?.ProductHandle))
        {
            return Results.BadRequest(new { Message = "ProductHandle is required." });
        }

        var command = new SubscribeCommand(
            applicationUser.Id,
            applicationUser.Email ?? applicationUser.UserName ?? string.Empty,
            GetDisplayName(applicationUser.UserName),
            string.Empty,
            request.ProductHandle);

        try
        {
            var result = await subscriptionService.SubscribeAsync(command);
            var response = new SubscriptionCreateResponse(request.CorrelationId())
            {
                AlreadySubscribed = result.AlreadySubscribed,
                Subscription = result.Subscription
            };
            return Results.Ok(response);
        }
        catch (SubscriptionPlanNotFoundException ex)
        {
            return Results.NotFound(new { Message = ex.Message });
        }
        catch (MaxioApiException ex)
        {
            var statusCode = ex.StatusCode is >= 400 and < 500 ? ex.StatusCode : 502;
            return Results.Problem(title: "Maxio API error", detail: ex.Message, statusCode: statusCode);
        }
        catch (MaxioConfigurationException ex)
        {
            return Results.Problem(title: "Billing system is not configured", detail: ex.Message, statusCode: 500);
        }
    }

    private static string GetDisplayName(string? userName)
    {
        if (string.IsNullOrWhiteSpace(userName))
        {
            return string.Empty;
        }

        var atIndex = userName.IndexOf('@');
        return atIndex > 0 ? userName.Substring(0, atIndex) : userName;
    }
}