using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class ListSubscriptionPlansEndpoint : IEndpoint<IResult, IMaxioService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/subscription-plans",
            async (IMaxioService maxioService) =>
            {
                return await HandleAsync(maxioService);
            })
            .WithName("ListSubscriptionPlans")
            .Produces<ListSubscriptionPlansResponse>()
            .Produces(StatusCodes.Status401Unauthorized)
            .WithTags("SubscriptionEndpoints");
    }

    public async Task<IResult> HandleAsync(IMaxioService maxioService)
    {
        var response = new ListSubscriptionPlansResponse();

        try
        {
            var plans = await maxioService.GetSubscriptionPlansAsync();
            response.Plans.AddRange(plans.Select(p => new SubscriptionPlanDto
            {
                Id = p.Id,
                Handle = p.Handle,
                Name = p.Name,
                PriceInCents = p.PriceInCents,
                PriceFormatted = FormatPrice(p.PriceInCents),
                Description = p.Description
            }));

            return Results.Ok(response);
        }
        catch (Exception ex)
        {
            return Results.BadRequest(new { error = ex.Message });
        }
    }

    private string FormatPrice(int cents)
    {
        var dollars = cents / 100m;
        return $"${dollars:F2}";
    }
}

public class SubscriptionPlanDto
{
    public int Id { get; set; }
    public string? Handle { get; set; }
    public string? Name { get; set; }
    public int PriceInCents { get; set; }
    public string? PriceFormatted { get; set; }
    public string? Description { get; set; }
}

public class ListSubscriptionPlansResponse
{
    public List<SubscriptionPlanDto> Plans { get; set; } = new();
}
