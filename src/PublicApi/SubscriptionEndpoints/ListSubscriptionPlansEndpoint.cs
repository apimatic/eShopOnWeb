using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using AutoMapper;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Services.Subscriptions;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class ListSubscriptionPlansEndpoint : IEndpoint<IResult, ListSubscriptionPlansRequest>
{
    private readonly ISubscriptionService _subscriptionService;
    private readonly IMapper _mapper;

    public ListSubscriptionPlansEndpoint(ISubscriptionService subscriptionService, IMapper mapper)
    {
        _subscriptionService = subscriptionService;
        _mapper = mapper;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/subscription-plans",
            async () =>
            {
                return await HandleAsync(new ListSubscriptionPlansRequest());
            })
            .Produces<ListSubscriptionPlansResponse>()
            .WithTags("SubscriptionEndpoints");
    }

    public async Task<IResult> HandleAsync(ListSubscriptionPlansRequest request)
    {
        var response = new ListSubscriptionPlansResponse(request.CorrelationId());

        try
        {
            var plans = await _subscriptionService.GetAvailablePlansAsync();
            response.Plans.AddRange(_mapper.Map<List<SubscriptionPlanDto>>(plans));
        }
        catch (Exception ex)
        {
            response.Message = $"Error loading plans: {ex.Message}";
        }

        return Results.Ok(response);
    }
}

public class ListSubscriptionPlansRequest : BaseRequest
{
}
