using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.Infrastructure.Identity;
using Microsoft.Extensions.Options;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// List the authenticated user's subscriptions from Maxio.
/// </summary>
public class MySubscriptionsEndpoint : IEndpoint<IResult, MaxioApiClient>
{
    private readonly MaxioConfiguration _config;
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly IHttpContextAccessor _httpContextAccessor;

    public MySubscriptionsEndpoint(
        IOptions<MaxioConfiguration> config,
        UserManager<ApplicationUser> userManager,
        IHttpContextAccessor httpContextAccessor)
    {
        _config = config.Value;
        _userManager = userManager;
        _httpContextAccessor = httpContextAccessor;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/my-subscriptions",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (MaxioApiClient maxioClient) =>
            {
                return await HandleAsync(maxioClient);
            })
            .Produces<MySubscriptionsResponse>()
            .WithTags("SubscriptionEndpoints");
    }

    public async Task<IResult> HandleAsync(MaxioApiClient maxioClient)
    {
        var response = new MySubscriptionsResponse();

        var httpContext = _httpContextAccessor.HttpContext;
        var userId = httpContext?.User.FindFirstValue(ClaimTypes.Name);
        if (string.IsNullOrEmpty(userId))
            return Results.Unauthorized();

        var user = await _userManager.FindByNameAsync(userId);
        if (user == null)
            return Results.Unauthorized();

        var customerReference = $"eshop-{user.Id}";
        var customer = await maxioClient.FindCustomerByReferenceAsync(customerReference);

        if (customer?.Customer == null)
        {
            response.Subscriptions = new List<SubscriptionDto>();
            return Results.Ok(response);
        }

        var allSubscriptions = await maxioClient.ListSubscriptionsAsync();
        if (allSubscriptions == null)
        {
            return Results.StatusCode(502);
        }

        var userSubscriptions = allSubscriptions
            .Where(s => s.Subscription?.Customer?.Reference == customerReference)
            .Select(s => new SubscriptionDto
            {
                Id = s.Subscription!.Id,
                State = s.Subscription.State,
                Price = s.Subscription.ProductPriceInCents / 100m,
                Currency = s.Subscription.Currency,
                CurrentPeriodEndsAt = s.Subscription.CurrentPeriodEndsAt,
                NextAssessmentAt = s.Subscription.NextAssessmentAt,
                ActivatedAt = s.Subscription.ActivatedAt,
                CreatedAt = s.Subscription.CreatedAt,
                CanceledAt = s.Subscription.CanceledAt,
                ProductName = s.Subscription.Product?.Name,
                ProductHandle = s.Subscription.Product?.Handle
            })
            .ToList();

        response.Subscriptions = userSubscriptions;
        return Results.Ok(response);
    }
}

public class MySubscriptionsResponse : BaseResponse
{
    public MySubscriptionsResponse(Guid correlationId) : base(correlationId) { }
    public MySubscriptionsResponse() { }

    public List<SubscriptionDto> Subscriptions { get; set; } = new();
}
