using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.PublicApi.Subscriptions;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// Lists the authenticated user's subscriptions as recorded in Maxio Advanced Billing.
/// </summary>
public class ListMySubscriptionsEndpoint : IEndpoint<IResult, ISubscriptionService>
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
            (ISubscriptionService subscriptionService) =>
            {
                return await HandleAsync(subscriptionService);
            })
           .Produces<ListMySubscriptionsResponse>()
           .ProducesProblem(401)
           .WithTags("SubscriptionEndpoints");
    }

    public async Task<IResult> HandleAsync(ISubscriptionService subscriptionService)
    {
        var response = new ListMySubscriptionsResponse();

        var userName = _httpContextAccessor.HttpContext?.User?.Identity?.Name;
        if (string.IsNullOrWhiteSpace(userName))
        {
            return Results.Unauthorized();
        }

        var subscriptions = await subscriptionService.GetSubscriptionsForUserAsync(userName);

        response.Subscriptions.AddRange(subscriptions.Select(s => new SubscriptionDetailsDto
        {
            MaxioSubscriptionId = s.MaxioSubscriptionId,
            State = s.State,
            PlanHandle = s.PlanHandle,
            PlanName = s.PlanName,
            Price = s.Price,
            PriceFormatted = s.PriceFormatted,
            NextBillingDate = s.NextBillingDate,
            CustomerReference = s.CustomerReference,
            PaymentCollectionMethod = s.PaymentCollectionMethod,
            ActivatedAt = s.ActivatedAt,
            CreatedAt = s.CreatedAt
        }));

        return Results.Ok(response);
    }
}