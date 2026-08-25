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
using Microsoft.Extensions.Caching.Memory;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// Lists the subscription plans (Maxio products) available in the configured product family.
/// </summary>
public class SubscriptionPlanListEndpoint : IEndpoint<IResult, MaxioClient>
{
    private static readonly TimeSpan CacheDuration = TimeSpan.FromMinutes(5);
    private const string CacheKey = "maxio-subscription-plans";

    private readonly IMemoryCache _cache;

    public SubscriptionPlanListEndpoint(IMemoryCache cache)
    {
        _cache = cache;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/subscription-plans",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (MaxioClient maxioClient) =>
            {
                return await HandleAsync(maxioClient);
            })
            .Produces<ListSubscriptionPlansResponse>()
            .WithTags("SubscriptionEndpoints");
    }

    public async Task<IResult> HandleAsync(MaxioClient maxioClient)
    {
        var response = new ListSubscriptionPlansResponse();

        var plans = await _cache.GetOrCreateAsync(CacheKey, async entry =>
        {
            entry.AbsoluteExpirationRelativeToNow = CacheDuration;
            var products = await maxioClient.ListPlansAsync();
            return products.Select(SubscriptionMapper.ToDto).ToList();
        });

        response.SubscriptionPlans.AddRange(plans ?? new List<SubscriptionPlanDto>());
        return Results.Ok(response);
    }
}

public class ListSubscriptionPlansResponse : BaseResponse
{
    public List<SubscriptionPlanDto> SubscriptionPlans { get; set; } = new();
}
