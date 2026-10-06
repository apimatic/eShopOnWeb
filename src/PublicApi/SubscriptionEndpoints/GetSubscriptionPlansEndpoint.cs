using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.PublicApi.Maxio;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// Lists the subscription plans available in the configured Maxio product family
/// </summary>
public class GetSubscriptionPlansEndpoint : IEndpoint<IResult, GetSubscriptionPlansRequest, IMaxioSubscriptionService>
{
    private readonly IHttpContextAccessor _httpContextAccessor;

    public GetSubscriptionPlansEndpoint(IHttpContextAccessor httpContextAccessor)
    {
        _httpContextAccessor = httpContextAccessor;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/subscription-plans",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (IMaxioSubscriptionService subscriptionService) =>
            {
                return await HandleAsync(new GetSubscriptionPlansRequest(), subscriptionService);
            })
            .Produces<GetSubscriptionPlansResponse>()
            .WithTags("SubscriptionEndpoints");
    }

    public async Task<IResult> HandleAsync(GetSubscriptionPlansRequest request, IMaxioSubscriptionService subscriptionService)
    {
        var catalog = await subscriptionService.GetPlanCatalogAsync(_httpContextAccessor.HttpContext?.RequestAborted ?? System.Threading.CancellationToken.None);
        var response = new GetSubscriptionPlansResponse(request.CorrelationId())
        {
            ProductFamilyHandle = catalog.ProductFamilyHandle,
            Plans = catalog.Plans.Select(p => new SubscriptionPlanDto
            {
                Handle = p.Handle,
                Name = p.Name,
                Description = p.Description,
                PriceInCents = p.PriceInCents,
                PriceDisplay = p.PriceDisplay,
                Interval = p.Interval,
                IntervalUnit = p.IntervalUnit,
                RequireCreditCard = p.RequireCreditCard
            }).ToList(),
            Components = catalog.Components.Select(c => new SubscriptionComponentDto
            {
                Handle = c.Handle,
                Name = c.Name,
                Kind = c.Kind,
                UnitName = c.UnitName,
                PricePerUnitInCents = c.PricePerUnitInCents
            }).ToList()
        };
        return Results.Ok(response);
    }
}