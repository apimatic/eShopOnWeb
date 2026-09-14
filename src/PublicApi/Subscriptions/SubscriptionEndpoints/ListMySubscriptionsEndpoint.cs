using System;
using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.Subscriptions.SubscriptionEndpoints;

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
            async (ISubscriptionService subscriptionService) =>
            {
                return await HandleAsync(subscriptionService);
            })
            .RequireAuthorization()
            .Produces<ListMySubscriptionsResponse>()
            .WithTags("SubscriptionEndpoints");
    }

    public async Task<IResult> HandleAsync(ISubscriptionService subscriptionService)
    {
        var userName = _httpContextAccessor.HttpContext?.User.FindFirstValue(ClaimTypes.Name);
        if (string.IsNullOrEmpty(userName))
        {
            return Results.Unauthorized();
        }

        var response = new ListMySubscriptionsResponse(Guid.NewGuid());
        try
        {
            response.Subscriptions.AddRange(await subscriptionService.ListMySubscriptionsAsync(userName));
            return Results.Ok(response);
        }
        catch (MaxioConfigurationException)
        {
            throw;
        }
        catch (MaxioApiException ex)
        {
            return ErrorResult.From(ex);
        }
    }
}
