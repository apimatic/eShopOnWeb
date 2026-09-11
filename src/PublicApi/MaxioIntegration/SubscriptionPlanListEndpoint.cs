using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.MaxioIntegration;

public class SubscriptionPlanListEndpoint : IEndpoint<IResult, SubscriptionPlanListRequest>
{
    private readonly IMaxioApiClient _maxioClient;
    private readonly MaxioSettings _settings;

    public SubscriptionPlanListEndpoint(IMaxioApiClient maxioClient, Microsoft.Extensions.Options.IOptions<MaxioSettings> settings)
    {
        _maxioClient = maxioClient;
        _settings = settings.Value;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/subscription-plans", async (HttpRequest request) =>
        {
            return await HandleAsync(new SubscriptionPlanListRequest(), request.HttpContext.RequestServices);
        })
        .Produces<SubscriptionPlanListResponse>()
        .WithTags("SubscriptionEndpoints");
    }

    public async Task<IResult> HandleAsync(SubscriptionPlanListRequest request, IServiceProvider serviceProvider)
    {
        var response = new SubscriptionPlanListResponse(request.CorrelationId());

        try
        {
            var plans = await _maxioClient.GetProductsByFamilyAsync(_settings.ProductFamilyHandle);
            response.Plans = plans.Select(p => new SubscriptionPlanDto
            {
                Id = p.Id,
                Name = p.Name,
                Handle = p.Handle,
                Description = p.Description ?? string.Empty,
                PriceInCents = p.PriceInCents,
                Interval = p.Interval,
                IntervalUnit = p.IntervalUnit
            }).ToList();
            response.IsSuccess = true;
        }
        catch (Exception ex)
        {
            response.IsSuccess = false;
            response.ErrorMessage = $"Failed to load subscription plans: {ex.Message}";
        }

        return Results.Ok(response);
    }
}
