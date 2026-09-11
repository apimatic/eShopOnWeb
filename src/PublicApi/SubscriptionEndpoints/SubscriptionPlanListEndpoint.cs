using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.PublicApi.Services;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class SubscriptionPlanListEndpoint : IEndpoint<IResult, object>
{
    private readonly IMaxioSubscriptionService _service;

    public SubscriptionPlanListEndpoint(IMaxioSubscriptionService service)
    {
        _service = service;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/subscription-plans", async () =>
        {
            return await HandleAsync(new object());
        })
        .Produces<List<SubscriptionPlanDto>>()
        .RequireAuthorization()
        .WithTags("SubscriptionEndpoints");
    }

    public async Task<IResult> HandleAsync(object request)
    {
        try
        {
            var products = await _service.ListPlansAsync();
            var dtos = products.Select(p => new SubscriptionPlanDto
            {
                Id = p.Product?.Id ?? 0,
                Handle = p.Product?.Handle ?? string.Empty,
                Name = p.Product?.Name ?? string.Empty,
                Price = 0,
                PriceUnit = string.Empty
            }).ToList();
            return Results.Ok(dtos);
        }
        catch (Exception ex)
        {
            return Results.Problem(ex.Message);
        }
    }
}
