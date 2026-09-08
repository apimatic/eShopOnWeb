using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints.Maxio;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// Lists the subscriptions of the authenticated eShopOnWeb user, as recorded
/// in Maxio Advanced Billing (plan, price, state, next billing date).
/// </summary>
public class MySubscriptionsEndpoint : IEndpoint<IResult, MySubscriptionsRequest, ISubscriptionService>
{
    public MySubscriptionsEndpoint(ISubscriptionService subscriptionService, IHttpContextAccessor httpContextAccessor)
    {
        SubscriptionService = subscriptionService;
        HttpContextAccessor = httpContextAccessor;
    }

    private ISubscriptionService SubscriptionService { get; }

    private IHttpContextAccessor HttpContextAccessor { get; }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/my-subscriptions",
            async (ISubscriptionService subscriptionService) =>
            {
                return await HandleAsync(new MySubscriptionsRequest(), subscriptionService);
            })
            .Produces<MySubscriptionsResponse>()
            .RequireAuthorization(MaxioEndpointResults.JwtAuthorize())
            .WithTags("SubscriptionEndpoints");
    }

    public async Task<IResult> HandleAsync(MySubscriptionsRequest request, ISubscriptionService subscriptionService)
    {
        var httpContext = HttpContextAccessor.HttpContext;
        var user = httpContext?.User;
        var userName = user?.Identity?.Name;
        if (string.IsNullOrEmpty(userName) || !(user?.Identity?.IsAuthenticated ?? false))
        {
            return Results.Unauthorized();
        }

        try
        {
            var subscriptions = await SubscriptionService.GetMySubscriptionsAsync(userName, CancellationToken.None);
            return Results.Ok(new MySubscriptionsResponse(request.CorrelationId())
            {
                Subscriptions = subscriptions
            });
        }
        catch (UnauthorizedAccessException)
        {
            return Results.Unauthorized();
        }
        catch (MaxioApiException ex)
        {
            return MaxioEndpointResults.FromMaxioException(ex);
        }
    }
}
