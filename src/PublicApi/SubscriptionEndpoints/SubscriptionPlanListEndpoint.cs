using System.Linq;
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
/// Lists the subscription plans a shopper can subscribe to.
/// </summary>
public class SubscriptionPlanListEndpoint : IEndpoint<IResult, ISubscriptionApiService, CancellationToken>
{
    private readonly IMapper _mapper;
    private readonly ILogger<SubscriptionPlanListEndpoint> _logger;

    public SubscriptionPlanListEndpoint(IMapper mapper, ILogger<SubscriptionPlanListEndpoint> logger)
    {
        _mapper = mapper;
        _logger = logger;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/subscription-plans",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (ISubscriptionApiService subscriptions, CancellationToken cancellationToken) =>
            {
                return await HandleAsync(subscriptions, cancellationToken);
            })
            .Produces<ListSubscriptionPlansResponse>()
            .ProducesProblem(StatusCodes.Status502BadGateway)
            .ProducesProblem(StatusCodes.Status503ServiceUnavailable)
            .WithTags("SubscriptionEndpoints");
    }

    public async Task<IResult> HandleAsync(ISubscriptionApiService subscriptions, CancellationToken cancellationToken)
    {
        var response = new ListSubscriptionPlansResponse();

        return await SubscriptionProblems.ExecuteAsync(async () =>
        {
            var plans = await subscriptions.ListPlansAsync(cancellationToken);
            response.Plans.AddRange(plans.Select(_mapper.Map<SubscriptionPlanDto>));

            return Results.Ok(response);
        }, _logger, response.CorrelationId());
    }
}
