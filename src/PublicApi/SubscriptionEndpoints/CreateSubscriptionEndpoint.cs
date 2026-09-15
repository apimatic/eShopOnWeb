using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Billing;
using Microsoft.eShopWeb.Infrastructure.Identity;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// Enroll the authenticated shopper in a subscription plan
/// </summary>
public class CreateSubscriptionEndpoint : IEndpoint<IResult, CreateSubscriptionRequest, ISubscriptionBillingService>
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
            (CreateSubscriptionRequest request, ISubscriptionBillingService billing) =>
            {
                return await HandleAsync(request, billing);
            })
            .Produces<CreateSubscriptionResponse>(StatusCodes.Status201Created)
            .Produces<CreateSubscriptionResponse>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status400BadRequest)
            .Produces(StatusCodes.Status401Unauthorized)
            .WithTags("SubscriptionEndpoints");
    }

    public async Task<IResult> HandleAsync(CreateSubscriptionRequest request, ISubscriptionBillingService billing)
    {
        var response = new CreateSubscriptionResponse(request.CorrelationId());
        var httpContext = _httpContextAccessor.HttpContext;
        var userName = httpContext?.User.Identity?.Name;
        if (string.IsNullOrWhiteSpace(userName))
        {
            return Results.Unauthorized();
        }

        if (string.IsNullOrWhiteSpace(request.ProductHandle))
        {
            return Results.BadRequest();
        }

        var user = await _userManager.FindByNameAsync(userName);
        var email = user?.Email ?? userName;
        var (firstName, lastName) = SplitDisplayName(userName);

        var result = await billing.SubscribeAsync(
            new SubscribeToPlan(userName, email, firstName, lastName, request.ProductHandle.Trim()),
            httpContext?.RequestAborted ?? CancellationToken.None);

        response.Subscription = Map(result.Subscription);
        response.Created = result.Created;

        if (result.Created)
        {
            return Results.Created("api/my-subscriptions", response);
        }

        return Results.Ok(response);
    }

    private static UserSubscriptionDto Map(UserSubscription subscription) => new()
    {
        Id = subscription.Id,
        ProductHandle = subscription.ProductHandle,
        ProductName = subscription.ProductName,
        Price = subscription.Price,
        State = subscription.State,
        NextBillingDate = subscription.NextBillingDate
    };

    private static (string FirstName, string LastName) SplitDisplayName(string userName)
    {
        var local = userName;
        var at = userName.IndexOf('@');
        if (at > 0)
        {
            local = userName[..at];
        }

        if (string.IsNullOrWhiteSpace(local))
        {
            local = "Shopper";
        }

        return (local, "eShop");
    }
}
