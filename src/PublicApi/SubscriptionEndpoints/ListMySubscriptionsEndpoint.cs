using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// Lists the authenticated shopper's subscriptions. The caller is identified from the
/// JWT; a shopper only ever sees their own subscriptions.
/// </summary>
public class ListMySubscriptionsEndpoint : IEndpoint<IResult, IMaxioSubscriptionService>
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
            (IMaxioSubscriptionService subscriptionService) =>
            {
                return await HandleAsync(subscriptionService);
            })
            .Produces<MySubscriptionsResponse>()
            .ProducesProblem(StatusCodes.Status502BadGateway)
            .WithTags("SubscriptionEndpoints");
    }

    public async Task<IResult> HandleAsync(IMaxioSubscriptionService subscriptionService)
    {
        var httpContext = _httpContextAccessor.HttpContext;
        var cancellationToken = httpContext?.RequestAborted ?? CancellationToken.None;

        var subscriber = SubscriberFactory.FromPrincipal(httpContext?.User);
        if (subscriber is null)
        {
            return Results.Unauthorized();
        }

        var response = new MySubscriptionsResponse();

        try
        {
            var subscriptions = await subscriptionService.GetSubscriptionsAsync(subscriber, cancellationToken);
            response.Subscriptions = subscriptions.Select(CustomerSubscriptionDto.From).ToList();
            return Results.Ok(response);
        }
        catch (MaxioIntegrationException ex)
        {
            return SubscriptionProblem.From(ex, "Unable to list your subscriptions");
        }
    }
}
