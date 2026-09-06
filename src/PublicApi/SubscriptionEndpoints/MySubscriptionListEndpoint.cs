using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using AutoMapper;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// List the signed-in shopper's own subscriptions.
/// </summary>
public class MySubscriptionListEndpoint : IEndpoint<IResult, ISubscriptionApiService, CancellationToken>
{
    private readonly IMapper _mapper;

    public MySubscriptionListEndpoint(IMapper mapper)
    {
        _mapper = mapper;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/my-subscriptions",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (ISubscriptionApiService subscriptions, CancellationToken cancellationToken) =>
            {
                return await HandleAsync(subscriptions, cancellationToken);
            })
            .Produces<ListMySubscriptionsResponse>()
            .WithTags("SubscriptionEndpoints");
    }

    public async Task<IResult> HandleAsync(ISubscriptionApiService subscriptions, CancellationToken cancellationToken)
    {
        var response = new ListMySubscriptionsResponse
        {
            CustomerReference = await subscriptions.GetBillingReferenceAsync()
        };

        var subscriptionsForCaller = await subscriptions.ListMySubscriptionsAsync(cancellationToken);
        response.Subscriptions.AddRange(subscriptionsForCaller.Select(_mapper.Map<SubscriptionDto>));

        return Results.Ok(response);
    }
}
