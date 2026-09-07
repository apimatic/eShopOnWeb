using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.PublicApi.Services;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class ListSubscriptionPlansEndpoint : IEndpoint<IResult, EmptyRequest, IMaxioService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/subscription-plans",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
            async (IMaxioService maxioService) =>
            {
                var endpoint = new ListSubscriptionPlansEndpoint();
                return await endpoint.HandleAsync(new EmptyRequest(), maxioService);
            })
            .Produces<ListSubscriptionPlansResponse>()
            .WithTags("SubscriptionEndpoints");
    }

    public async Task<IResult> HandleAsync(EmptyRequest request, IMaxioService maxioService)
    {
        var response = new ListSubscriptionPlansResponse(request.CorrelationId());

        try
        {
            var plans = await maxioService.ListProductsAsync();
            response.Plans.AddRange(plans.Select(p => new SubscriptionPlanResponse
            {
                Id = p.Id,
                Handle = p.Handle,
                Name = p.Name,
                Description = p.Description,
                PriceInCents = p.PriceInCents,
                PriceFormatted = $"${p.PriceInCents / 100m:F2}",
                Interval = p.Interval,
                IntervalUnit = p.IntervalUnit
            }));
        }
        catch (Exception ex)
        {
            return Results.StatusCode(500);
        }

        return Results.Ok(response);
    }
}

public class EmptyRequest : BaseRequest
{
}

public class ListSubscriptionPlansResponse : BaseResponse
{
    public ListSubscriptionPlansResponse(Guid correlationId) : base(correlationId)
    {
    }

    public List<SubscriptionPlanResponse> Plans { get; } = new();
}

public class SubscriptionPlanResponse
{
    public int Id { get; set; }
    public string Handle { get; set; } = "";
    public string Name { get; set; } = "";
    public string Description { get; set; } = "";
    public decimal PriceInCents { get; set; }
    public string PriceFormatted { get; set; } = "";
    public int Interval { get; set; }
    public string IntervalUnit { get; set; } = "";
}
