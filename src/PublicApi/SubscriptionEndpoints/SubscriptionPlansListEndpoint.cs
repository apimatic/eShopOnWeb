using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using AutoMapper;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.PublicApi.Maxio;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// Lists the subscribable subscription plans (GET /api/subscription-plans)
/// </summary>
public class SubscriptionPlansListEndpoint : IEndpoint<IResult, CancellationToken>
{
    private readonly IMaxioBillingService _billingService;
    private readonly IMapper _mapper;

    public SubscriptionPlansListEndpoint(IMaxioBillingService billingService, IMapper mapper)
    {
        _billingService = billingService;
        _mapper = mapper;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/subscription-plans",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (CancellationToken ct) =>
            {
                return await HandleAsync(ct);
            })
            .Produces<ListSubscriptionPlansResponse>()
            .WithTags("SubscriptionEndpoints");
    }

    public async Task<IResult> HandleAsync(CancellationToken cancellationToken)
    {
        var response = new ListSubscriptionPlansResponse();

        try
        {
            var plans = await _billingService.ListPlansAsync(cancellationToken);
            response.SubscriptionPlans.AddRange(plans.Select(_mapper.Map<SubscriptionPlanDto>));

            return Results.Ok(response);
        }
        catch (Exception ex) when (MaxioEndpointResult.TryMapError(ex) is { } error)
        {
            return error;
        }
    }
}
