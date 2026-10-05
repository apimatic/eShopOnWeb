using System;
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

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// Subscribes the signed-in shopper to a subscription plan. Idempotent per
/// shopper+plan: a repeated request returns the existing subscription.
/// </summary>
public class CreateSubscriptionEndpoint : IEndpoint<IResult, CreateSubscriptionRequest, UserManager<ApplicationUser>>
{
    private readonly IMaxioBillingService _billingService;
    private readonly IHttpContextAccessor _httpContextAccessor;

    public CreateSubscriptionEndpoint(IMaxioBillingService billingService, IHttpContextAccessor httpContextAccessor)
    {
        _billingService = billingService;
        _httpContextAccessor = httpContextAccessor;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/subscriptions",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (CreateSubscriptionRequest request, UserManager<ApplicationUser> userManager) =>
            {
                return await HandleAsync(request, userManager);
            })
            .Produces<CreateSubscriptionResponse>()
            .Produces(400)
            .Produces(401)
            .Produces(404)
            .Produces(409)
            .Produces(502)
            .WithTags("SubscriptionEndpoints");
    }

    public async Task<IResult> HandleAsync(CreateSubscriptionRequest request, UserManager<ApplicationUser> userManager)
    {
        var response = new CreateSubscriptionResponse();

        var principal = _httpContextAccessor.HttpContext?.User;
        var userName = principal?.Identity?.Name;
        if (string.IsNullOrWhiteSpace(userName))
        {
            return Results.Json(new { error = "The caller's identity could not be resolved from the token." }, statusCode: 401);
        }

        try
        {
            var user = await userManager.FindByNameAsync(userName);
            if (user is null)
            {
                return Results.Json(new { error = "The caller's identity could not be resolved from the token." }, statusCode: 401);
            }

            var subscriber = new MaxioSubscriberIdentity(user.Id, user.UserName, user.Email);
            var subscription = await _billingService.SubscribeAsync(subscriber, request.ProductHandle ?? string.Empty, CancellationToken.None);

            response.Subscription = subscription;
            return subscription.Created
                ? Results.Created($"api/my-subscriptions/{subscription.MaxioSubscriptionId}", response)
                : Results.Ok(response);
        }
        catch (MaxioBillingException ex)
        {
            return MaxioEndpointResults.Failure(ex);
        }
    }
}