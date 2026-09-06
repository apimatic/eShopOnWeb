using System.Linq;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using AutoMapper;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// Lists the authenticated shopper's subscriptions. The shopper is identified by the bearer token, so
/// there is no way to read somebody else's subscriptions.
/// </summary>
public class ListMySubscriptionsEndpoint : IEndpoint<IResult, ListMySubscriptionsRequest, ISubscriptionService>
{
    private readonly IMapper _mapper;

    public ListMySubscriptionsEndpoint(IMapper mapper)
    {
        _mapper = mapper;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/my-subscriptions",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (ClaimsPrincipal user, ISubscriptionService subscriptionService, CancellationToken cancellationToken) =>
            {
                var userName = SubscriberIdentity.GetUserName(user);
                if (string.IsNullOrWhiteSpace(userName))
                {
                    return Results.Unauthorized();
                }

                var request = new ListMySubscriptionsRequest(SubscriberIdentity.ToUserKey(userName!), cancellationToken);
                return await HandleAsync(request, subscriptionService);
            })
            .Produces<ListMySubscriptionsResponse>()
            .WithTags("SubscriptionEndpoints");
    }

    public async Task<IResult> HandleAsync(ListMySubscriptionsRequest request, ISubscriptionService subscriptionService)
    {
        var response = new ListMySubscriptionsResponse(request.CorrelationId());

        var result = await subscriptionService.GetSubscriptionsAsync(request.UserKey, request.CancellationToken);

        response.Customer = result.Customer is null ? null : _mapper.Map<BillingCustomerDto>(result.Customer);
        response.Subscriptions.AddRange(result.Subscriptions.Select(_mapper.Map<SubscriptionDto>));
        response.ActiveCount = result.Subscriptions.Count(s => s.IsLive);

        return Results.Ok(response);
    }
}
