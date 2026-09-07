using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using MaxioAdvancedBilling.Core.Exceptions;
using MaxioAdvancedBilling.Core.ErrorResponse;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Caching.Memory;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class ListSubscriptionPlansEndpoint : IEndpoint<IResult>
{
    private readonly MaxioAdvancedBilling.MaxioAdvancedBillingClient _maxioClient;
    private readonly IMemoryCache _cache;

    public ListSubscriptionPlansEndpoint(
        MaxioAdvancedBilling.MaxioAdvancedBillingClient maxioClient,
        IMemoryCache cache)
    {
        _maxioClient = maxioClient;
        _cache = cache;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/subscription-plans",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
            async () =>
            {
                return await HandleAsync();
            })
            .Produces<ListSubscriptionPlansResponse>()
            .WithTags("SubscriptionEndpoints");
    }

    public async Task<IResult> HandleAsync()
    {
        const string cacheKey = "subscription_plans";

        if (_cache.TryGetValue(cacheKey, out List<SubscriptionPlanDto>? cachedPlans))
        {
            return Results.Ok(new ListSubscriptionPlansResponse { Plans = cachedPlans! });
        }

        try
        {
            var response = await _maxioClient.Products.ListProducts(
                dateField: null,
                filter: null,
                endDate: null,
                endDatetime: null,
                startDate: null,
                startDatetime: null,
                includeArchived: null,
                include: null,
                page: 1,
                perPage: 100,
                ct: CancellationToken.None);

            var plans = response
                .Select(p => new SubscriptionPlanDto
                {
                    Id = (int)p.Product.Id,
                    Name = p.Product.Name ?? string.Empty,
                    Handle = p.Product.Handle ?? string.Empty,
                    Description = p.Product.Description,
                    PriceInCents = (int)(p.Product.PriceInCents ?? 0),
                    Interval = (int)(p.Product.Interval ?? 0),
                    IntervalUnit = p.Product.IntervalUnit?.ToString() ?? "month"
                })
                .ToList();

            _cache.Set(cacheKey, plans, TimeSpan.FromHours(24));

            return Results.Ok(new ListSubscriptionPlansResponse { Plans = plans });
        }
        catch (SdkException<RawError> ex)
        {
            return Results.StatusCode((int?)ex.Error.StatusCode ?? 500);
        }
        catch (JsonException)
        {
            return Results.StatusCode(500);
        }
    }

    public class ListSubscriptionPlansResponse
    {
        public List<SubscriptionPlanDto> Plans { get; set; } = new();
    }
}
