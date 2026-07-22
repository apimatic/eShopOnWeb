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
/// Lists the authenticated caller's own subscriptions
/// </summary>
public class MySubscriptionsEndpoint : IEndpoint<IResult, MySubscriptionsRequest, ISubscriptionService>
{
    private readonly IMapper _mapper;

    public MySubscriptionsEndpoint(IMapper mapper)
    {
        _mapper = mapper;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/subscriptions",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (ClaimsPrincipal user, ISubscriptionService subscriptionService, CancellationToken cancellationToken) =>
            {
                var request = new MySubscriptionsRequest { UserReference = user.Identity?.Name };

                return await HandleAsync(request, subscriptionService, cancellationToken);
            })
            .Produces<MySubscriptionsResponse>()
            .WithTags("SubscriptionEndpoints");
    }

    public Task<IResult> HandleAsync(MySubscriptionsRequest request, ISubscriptionService subscriptionService)
    {
        return HandleAsync(request, subscriptionService, CancellationToken.None);
    }

    public async Task<IResult> HandleAsync(MySubscriptionsRequest request, ISubscriptionService subscriptionService, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.UserReference))
        {
            return Results.Unauthorized();
        }

        return await SubscriptionErrorResults.ExecuteAsync(async () =>
        {
            var response = new MySubscriptionsResponse(request.CorrelationId());

            var subscriptions = await subscriptionService.GetSubscriptionsAsync(request.UserReference, cancellationToken);
            response.Subscriptions.AddRange(subscriptions.Select(_mapper.Map<SubscriptionDto>));

            return Results.Ok(response);
        });
    }
}
