using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.PublicApi.Services;
using MinimalApi.Endpoint;
using Swashbuckle.AspNetCore.Annotations;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// List available subscription plans
/// </summary>
public class ListSubscriptionPlansEndpoint : IEndpoint<IResult, EmptyRequest, MaxioSubscriptionService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/subscription-plans",
            async (MaxioSubscriptionService maxioService) =>
            {
                return await HandleAsync(new EmptyRequest(), maxioService);
            })
            .Produces<ListSubscriptionPlansResponse>()
            .WithTags("SubscriptionEndpoints");
    }

    public async Task<IResult> HandleAsync(EmptyRequest request, MaxioSubscriptionService maxioService)
    {
        var response = new ListSubscriptionPlansResponse(Guid.NewGuid());

        try
        {
            var plans = await maxioService.ListSubscriptionPlansAsync();
            response.Plans = plans
                .Select(p => new SubscriptionPlanDto
                {
                    Id = p.Id,
                    Handle = p.Handle,
                    Name = p.Name,
                    Description = p.Description,
                    PriceInCents = p.PriceInCents,
                    Price = p.PriceInCents / 100m
                })
                .ToList();
        }
        catch (Exception ex)
        {
            response.ErrorMessage = ex.Message;
            return Results.BadRequest(response);
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

    public List<SubscriptionPlanDto> Plans { get; set; } = new List<SubscriptionPlanDto>();
    public string? ErrorMessage { get; set; }
}
