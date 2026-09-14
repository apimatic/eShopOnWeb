using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// Lists the subscriptions held by the signed-in shopper in Maxio (Advanced Billing).
/// Route: GET /api/my-subscriptions
/// </summary>
public class ListMySubscriptionsEndpoint : IEndpoint<IResult>
{
    private readonly ISubscriptionBillingService _subscriptionBillingService;
    private readonly IHttpContextAccessor _httpContextAccessor;

    public ListMySubscriptionsEndpoint(
        ISubscriptionBillingService subscriptionBillingService,
        IHttpContextAccessor httpContextAccessor)
    {
        _subscriptionBillingService = subscriptionBillingService;
        _httpContextAccessor = httpContextAccessor;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/my-subscriptions",
                [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] () => HandleAsync())
            .Produces<MySubscriptionsResponse>()
            .WithTags("SubscriptionEndpoints");
    }

    public async Task<IResult> HandleAsync()
    {
        var subscriberKey = SubscriberKey.From(_httpContextAccessor.HttpContext?.User);
        if (string.IsNullOrWhiteSpace(subscriberKey))
        {
            return Results.Unauthorized();
        }

        var cancellationToken = _httpContextAccessor.HttpContext?.RequestAborted ?? CancellationToken.None;
        var response = new MySubscriptionsResponse();
        var subscriptions = await _subscriptionBillingService.ListSubscriptionsAsync(subscriberKey, cancellationToken);
        response.Subscriptions.AddRange(subscriptions);
        return Results.Ok(response);
    }
}
