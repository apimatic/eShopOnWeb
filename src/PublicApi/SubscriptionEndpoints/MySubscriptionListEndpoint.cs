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
using Microsoft.Extensions.Logging;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// Lists the authenticated shopper's own subscriptions.
/// </summary>
public class MySubscriptionListEndpoint : IEndpoint<IResult, ClaimsPrincipal, ISubscriptionApiService, CancellationToken>
{
    private readonly IMapper _mapper;
    private readonly ILogger<MySubscriptionListEndpoint> _logger;

    public MySubscriptionListEndpoint(IMapper mapper, ILogger<MySubscriptionListEndpoint> logger)
    {
        _mapper = mapper;
        _logger = logger;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/my-subscriptions",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (ClaimsPrincipal user, ISubscriptionApiService subscriptions, CancellationToken cancellationToken) =>
            {
                return await HandleAsync(user, subscriptions, cancellationToken);
            })
            .Produces<ListMySubscriptionsResponse>()
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status502BadGateway)
            .ProducesProblem(StatusCodes.Status503ServiceUnavailable)
            .WithTags("SubscriptionEndpoints");
    }

    public async Task<IResult> HandleAsync(ClaimsPrincipal user, ISubscriptionApiService subscriptions, CancellationToken cancellationToken)
    {
        var response = new ListMySubscriptionsResponse();

        return await SubscriptionProblems.ExecuteAsync(async () =>
        {
            var subscriptionsForUser = await subscriptions.ListSubscriptionsAsync(user, cancellationToken);
            response.Subscriptions.AddRange(subscriptionsForUser.Select(_mapper.Map<CustomerSubscriptionDto>));

            return Results.Ok(response);
        }, _logger, response.CorrelationId());
    }
}
