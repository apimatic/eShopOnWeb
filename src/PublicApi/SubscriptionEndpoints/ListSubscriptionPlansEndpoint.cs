using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using AutoMapper;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class ListSubscriptionPlansEndpoint : IEndpoint<IResult, EmptyRequest>
{
    private readonly IMapper _mapper;

    public ListSubscriptionPlansEndpoint(IMapper mapper)
    {
        _mapper = mapper;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/subscription-plans",
            async (IMaxioBillingService billingService) =>
            {
                return await HandleAsync(new EmptyRequest(), billingService);
            })
            .WithName("GetSubscriptionPlans")
            .Produces<ListSubscriptionPlansResponse>()
            .WithTags("Subscriptions")
            .RequireAuthorization();
    }

    public async Task<IResult> HandleAsync(EmptyRequest request)
    {
        throw new NotImplementedException();
    }

    private async Task<IResult> HandleAsync(EmptyRequest request, IMaxioBillingService billingService)
    {
        var response = new ListSubscriptionPlansResponse();

        try
        {
            var plans = await billingService.GetAvailablePlansAsync();
            response.Plans.AddRange(_mapper.Map<List<SubscriptionPlanDto>>(plans));
        }
        catch
        {
            return Results.Problem("Error retrieving subscription plans", statusCode: StatusCodes.Status500InternalServerError);
        }

        return Results.Ok(response);
    }
}
