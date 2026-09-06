using System;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.Infrastructure.Services;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class SubscriptionPlansEndpoint : IEndpoint<IResult, SubscriptionPlansRequest, ISubscriptionService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/subscription-plans",
            async (ISubscriptionService subscriptionService) =>
            {
                return await HandleAsync(new SubscriptionPlansRequest(), subscriptionService);
            })
            .RequireAuthorization()
            .Produces<SubscriptionPlansResponse>()
            .WithTags("SubscriptionEndpoints");
    }

    public async Task<IResult> HandleAsync(SubscriptionPlansRequest request, ISubscriptionService subscriptionService)
    {
        var response = new SubscriptionPlansResponse(request.CorrelationId());

        try
        {
            var plans = await subscriptionService.GetPlansAsync();
            response.Plans = plans.Select(p => new SubscriptionPlanDto
            {
                Id = p.Id,
                Name = p.Name,
                Handle = p.Handle,
                PriceUSD = p.PriceUSD,
                BillingUnit = p.BillingUnit
            }).ToArray();

            return Results.Ok(response);
        }
        catch (Exception ex)
        {
            return Results.Problem(ex.Message, statusCode: 500);
        }
    }
}

public class SubscriptionPlansRequest : BaseRequest
{
}

public class SubscriptionPlansResponse : BaseResponse
{
    public SubscriptionPlansResponse(Guid correlationId) : base(correlationId)
    {
    }

    public SubscriptionPlanDto[] Plans { get; set; } = System.Array.Empty<SubscriptionPlanDto>();
}

public class SubscriptionPlanDto
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Handle { get; set; } = string.Empty;
    public decimal PriceUSD { get; set; }
    public string BillingUnit { get; set; } = string.Empty;
}
