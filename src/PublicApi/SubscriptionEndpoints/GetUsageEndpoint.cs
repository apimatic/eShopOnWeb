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

/// <summary>Read the running period-to-date usage for a subscription without recording any (UC2).</summary>
public class GetUsageEndpoint : IEndpoint<IResult, GetUsageRequest, ISubscriptionService>
{
    private readonly IMapper _mapper;

    public GetUsageEndpoint(IMapper mapper)
    {
        _mapper = mapper;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/subscriptions/{subscriptionId}/usage",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int subscriptionId, ISubscriptionService subscriptionService) =>
            {
                return await HandleAsync(new GetUsageRequest { SubscriptionId = subscriptionId }, subscriptionService);
            })
            .Produces<UsageResponse>()
            .WithTags("SubscriptionEndpoints");
    }

    public async Task<IResult> HandleAsync(GetUsageRequest request, ISubscriptionService subscriptionService)
    {
        var response = new UsageResponse(request.CorrelationId());

        var summary = await subscriptionService.GetUsageAsync(request.SubscriptionId);
        response.Usage = _mapper.Map<UsageSummaryDto>(summary);

        return Results.Ok(response);
    }
}
