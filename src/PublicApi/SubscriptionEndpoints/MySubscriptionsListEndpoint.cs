using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Subscriptions;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// Lists the authenticated shopper's subscriptions as reflected by the billing system of record.
/// </summary>
public class MySubscriptionsListEndpoint : IEndpoint<IResult, ISubscriptionBillingService>
{
    private readonly IHttpContextAccessor _httpContextAccessor;

    public MySubscriptionsListEndpoint(IHttpContextAccessor httpContextAccessor)
    {
        _httpContextAccessor = httpContextAccessor;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/my-subscriptions",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
            async (ISubscriptionBillingService billingService) => await HandleAsync(billingService))
            .Produces<MySubscriptionsResponse>()
            .ProducesProblem(StatusCodes.Status502BadGateway)
            .WithTags("SubscriptionEndpoints");
    }

    public async Task<IResult> HandleAsync(ISubscriptionBillingService billingService)
    {
        var httpContext = _httpContextAccessor.HttpContext;
        var subscriber = SubscriptionMapping.GetSubscriber(httpContext?.User!);
        if (subscriber is null)
        {
            return Results.Unauthorized();
        }

        var ct = httpContext?.RequestAborted ?? CancellationToken.None;
        try
        {
            var subscriptions = await billingService.GetSubscriptionsAsync(subscriber.Value, ct);
            var response = new MySubscriptionsResponse
            {
                Subscriptions = subscriptions.Select(s => s.ToDto()).ToList(),
            };
            return Results.Ok(response);
        }
        catch (SubscriptionBillingException ex)
        {
            return ex.ToResult();
        }
    }
}
