using System;
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
/// Lists the signed-in shopper's Maxio subscriptions.
/// </summary>
public class MySubscriptionsListEndpoint : IEndpoint<IResult, UserManager<ApplicationUser>>
{
    private readonly IMaxioBillingService _billingService;
    private readonly IHttpContextAccessor _httpContextAccessor;

    public MySubscriptionsListEndpoint(IMaxioBillingService billingService, IHttpContextAccessor httpContextAccessor)
    {
        _billingService = billingService;
        _httpContextAccessor = httpContextAccessor;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/my-subscriptions",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (UserManager<ApplicationUser> userManager) =>
            {
                return await HandleAsync(userManager);
            })
            .Produces<MySubscriptionsListResponse>()
            .Produces(401)
            .WithTags("SubscriptionEndpoints");
    }

    public async Task<IResult> HandleAsync(UserManager<ApplicationUser> userManager)
    {
        var response = new MySubscriptionsListResponse();

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
            var subscriptions = await _billingService.ListMySubscriptionsAsync(subscriber, default);
            response.Subscriptions.AddRange(subscriptions);
            return Results.Ok(response);
        }
        catch (MaxioBillingException ex)
        {
            return MaxioEndpointResults.Failure(ex);
        }
    }
}