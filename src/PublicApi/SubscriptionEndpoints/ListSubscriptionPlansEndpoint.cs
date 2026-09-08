using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Ardalis.ApiEndpoints;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.eShopWeb.PublicApi.Maxio;
using Microsoft.Extensions.Caching.Memory;
using Swashbuckle.AspNetCore.Annotations;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// Lists the plans (Maxio products) available to subscribe to.
/// </summary>
[Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
public class ListSubscriptionPlansEndpoint : EndpointBaseAsync
    .WithoutRequest
    .WithActionResult<SubscriptionPlansResponse>
{
    private const string CurrencyCacheKey = "maxio:site-currency";

    private readonly IMaxioBillingService _billingService;
    private readonly IMemoryCache _cache;

    public ListSubscriptionPlansEndpoint(IMaxioBillingService billingService, IMemoryCache cache)
    {
        _billingService = billingService;
        _cache = cache;
    }

    [HttpGet("api/subscription-plans")]
    [SwaggerOperation(
        Summary = "Lists available subscription plans",
        Description = "Lists the plans (products) available to subscribe to on the configured Maxio site.",
        OperationId = "subscriptions.listPlans",
        Tags = new[] { "SubscriptionEndpoints" })
    ]
    public override async Task<ActionResult<SubscriptionPlansResponse>> HandleAsync(CancellationToken cancellationToken = default)
    {
        var response = new SubscriptionPlansResponse();

        var plans = await _billingService.ListAvailablePlansAsync(cancellationToken);
        var currency = await GetCurrencyAsync(cancellationToken);

        response.Plans.AddRange(Map(plans, currency));
        return response;
    }

    private async Task<string> GetCurrencyAsync(CancellationToken cancellationToken)
    {
        if (_cache.TryGetValue(CurrencyCacheKey, out string? currency) && !string.IsNullOrEmpty(currency))
        {
            return currency!;
        }

        var siteCurrency = await _billingService.GetSiteCurrencyAsync(cancellationToken);
        if (!string.IsNullOrEmpty(siteCurrency))
        {
            _cache.Set(CurrencyCacheKey, siteCurrency, System.TimeSpan.FromMinutes(15));
            return siteCurrency!;
        }

        return string.Empty;
    }

    private static IEnumerable<SubscriptionPlanDto> Map(IReadOnlyList<MaxioProduct> plans, string currency)
    {
        var dtos = new List<SubscriptionPlanDto>(plans.Count);
        foreach (var plan in plans)
        {
            dtos.Add(SubscriptionMappings.ToDto(plan, currency));
        }

        return dtos;
    }
}
