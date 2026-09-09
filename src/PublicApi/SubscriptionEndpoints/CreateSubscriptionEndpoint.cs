using System;
using System.Collections.Generic;
using System.Net;
using System.Security.Claims;
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

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// Subscribes the authenticated user to a plan. Ensures a Maxio customer exists for the
/// user (idempotently) before enrolling them, so a double-click never creates
/// duplicate customers or subscriptions.
/// </summary>
public class CreateSubscriptionEndpoint : IEndpoint<IResult, CreateSubscriptionRequest, ISubscriptionService>
{
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly IHttpContextAccessor _httpContextAccessor;

    public CreateSubscriptionEndpoint(UserManager<ApplicationUser> userManager, IHttpContextAccessor httpContextAccessor)
    {
        _userManager = userManager;
        _httpContextAccessor = httpContextAccessor;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/subscriptions",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (CreateSubscriptionRequest request, ISubscriptionService subscriptionService) =>
            {
                return await HandleAsync(request, subscriptionService);
            })
            .Produces<CreateSubscriptionResponse>()
            .WithTags("SubscriptionEndpoints");
    }

    public async Task<IResult> HandleAsync(CreateSubscriptionRequest request, ISubscriptionService subscriptionService)
    {
        var response = new CreateSubscriptionResponse(request.CorrelationId());

        if (string.IsNullOrWhiteSpace(request?.ProductHandle))
        {
            return Results.ValidationProblem(new Dictionary<string, string[]>
            {
                { nameof(request.ProductHandle), new[] { "ProductHandle is required." } }
            });
        }

        var user = await ResolveUserAsync();
        if (user == null)
        {
            return Results.Unauthorized();
        }

        var email = !string.IsNullOrWhiteSpace(user.Email) ? user.Email : user.UserName!;

        SubscriptionDto subscription;
        try
        {
            subscription = await subscriptionService.SubscribeAsync(user.Id, email, request.ProductHandle);
        }
        catch (MaxioApiException ex) when (ex.StatusCode == (int)HttpStatusCode.NotFound)
        {
            return Results.NotFound(new { ex.Message });
        }
        catch (MaxioApiException ex) when (ex.StatusCode == (int)HttpStatusCode.UnprocessableEntity)
        {
            return Results.UnprocessableEntity(new { ex.Message });
        }
        catch (MaxioApiException ex)
        {
            return Results.Problem(
                statusCode: (int)HttpStatusCode.BadGateway,
                title: "Maxio subscription request failed.",
                detail: ex.Message);
        }

        response.Subscription = subscription;
        return Results.Created($"api/my-subscriptions/{subscription.SubscriptionId}", response);
    }

    private async Task<ApplicationUser?> ResolveUserAsync()
    {
        var userName = _httpContextAccessor.HttpContext?.User?.FindFirstValue(ClaimTypes.Name);
        if (string.IsNullOrEmpty(userName))
        {
            return null;
        }

        return await _userManager.FindByNameAsync(userName);
    }
}
