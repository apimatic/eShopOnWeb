using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using AutoMapper;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.Infrastructure.Services;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class ListSubscriptionPlansEndpoint : IEndpoint<IResult, EmptyRequest, ISubscriptionService>
{
    private readonly IMapper _mapper;

    public ListSubscriptionPlansEndpoint(IMapper mapper)
    {
        _mapper = mapper;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/subscription-plans",
            async (ISubscriptionService subscriptionService) =>
            {
                return await HandleAsync(new EmptyRequest(), subscriptionService);
            })
            .RequireAuthorization()
            .Produces<ListSubscriptionPlansResponse>()
            .WithTags("SubscriptionEndpoints")
            .WithName("GetSubscriptionPlans");
    }

    public async Task<IResult> HandleAsync(EmptyRequest request, ISubscriptionService subscriptionService)
    {
        try
        {
            var products = await subscriptionService.ListProductsAsync();
            var plans = products.Select(p => new SubscriptionPlanDto
            {
                Id = p.Id,
                Handle = p.Handle,
                Name = p.Name,
                Description = p.Description,
                Price = p.PriceInCents / 100m,
                BillingInterval = p.Interval,
                BillingIntervalUnit = p.IntervalUnit
            }).ToList();

            var response = new ListSubscriptionPlansResponse(request.CorrelationId())
            {
                Plans = plans
            };
            return Results.Ok(response);
        }
        catch (Exception ex)
        {
            return Results.Problem(
                detail: ex.Message,
                statusCode: StatusCodes.Status500InternalServerError);
        }
    }
}

public class ListSubscriptionPlansResponse : BaseResponse
{
    public ListSubscriptionPlansResponse() { }
    public ListSubscriptionPlansResponse(Guid correlationId) : base(correlationId) { }

    public List<SubscriptionPlanDto> Plans { get; set; } = new();
}

public class EmptyRequest : BaseRequest
{
}
