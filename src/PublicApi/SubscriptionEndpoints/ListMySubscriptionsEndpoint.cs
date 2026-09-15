using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Billing;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// List the authenticated shopper's subscriptions
/// </summary>
public class ListMySubscriptionsEndpoint : IEndpoint<IResult, ISubscriptionBillingService>
{
    private readonly IHttpContextAccessor _httpContextAccessor;

    public ListMySubscriptionsEndpoint(IHttpContextAccessor httpContextAccessor)
    {
        _httpContextAccessor = httpContextAccessor;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/my-subscriptions",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (ISubscriptionBillingService billing) =>
            {
                return await HandleAsync(billing);
            })
            .Produces<ListMySubscriptionsResponse>()
            .Produces(StatusCodes.Status401Unauthorized)
            .WithTags("SubscriptionEndpoints");
    }

    public async Task<IResult> HandleAsync(ISubscriptionBillingService billing)
    {
        var response = new ListMySubscriptionsResponse();
        var httpContext = _httpContextAccessor.HttpContext;
        var userName = httpContext?.User.Identity?.Name;
        if (string.IsNullOrWhiteSpace(userName))
        {
            return Results.Unauthorized();
        }

        var subscriptions = await billing.ListSubscriptionsForUserAsync(
            userName,
            httpContext?.RequestAborted ?? CancellationToken.None);

        response.Subscriptions.AddRange(subscriptions.Select(subscription => new UserSubscriptionDto
        {
            Id = subscription.Id,
            ProductHandle = subscription.ProductHandle,
            ProductName = subscription.ProductName,
            Price = subscription.Price,
            State = subscription.State,
            NextBillingDate = subscription.NextBillingDate
        }));

        return Results.Ok(response);
    }
}
