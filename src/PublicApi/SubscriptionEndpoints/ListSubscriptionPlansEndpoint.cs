using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Ardalis.ApiEndpoints;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Models;
using Microsoft.Extensions.Caching.Memory;
using Swashbuckle.AspNetCore.Annotations;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// Lists the subscription plans available for enrollment.
/// </summary>
[Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
public class ListSubscriptionPlansEndpoint : EndpointBaseAsync
    .WithoutRequest
    .WithActionResult<ListSubscriptionPlansResponse>
{
    private const string CacheKey = "subscription-plans";
    private static readonly TimeSpan CacheDuration = TimeSpan.FromMinutes(2);

    private readonly IMaxioBillingService _billingService;
    private readonly IMemoryCache _cache;

    public ListSubscriptionPlansEndpoint(IMaxioBillingService billingService, IMemoryCache cache)
    {
        _billingService = billingService;
        _cache = cache;
    }

    [HttpGet("api/subscription-plans")]
    [SwaggerOperation(
        Summary = "Lists the available subscription plans",
        Description = "Lists the available subscription plans",
        OperationId = "subscriptionPlans.list",
        Tags = new[] { "SubscriptionEndpoints" })
    ]
    public override async Task<ActionResult<ListSubscriptionPlansResponse>> HandleAsync(CancellationToken cancellationToken = default)
    {
        IReadOnlyList<SubscriptionPlanInfo> plans;
        try
        {
            plans = await _cache.GetOrCreateAsync(CacheKey, async entry =>
            {
                entry.AbsoluteExpirationRelativeToNow = CacheDuration;
                return await _billingService.ListPlansAsync(cancellationToken);
            }) ?? Array.Empty<SubscriptionPlanInfo>();
        }
        catch (MaxioBillingException ex)
        {
            return SubscriptionEndpointHelpers.BillingProblem(ex);
        }

        return new ListSubscriptionPlansResponse(Guid.NewGuid())
        {
            Plans = plans.Select(p => new SubscriptionPlanDto
            {
                Handle = p.Handle,
                Name = p.Name,
                Description = p.Description,
                Price = p.Price,
                Interval = p.Interval,
                IntervalUnit = p.IntervalUnit,
                RequireCreditCard = p.RequireCreditCard
            }).ToList()
        };
    }
}

internal static class SubscriptionEndpointHelpers
{
    /// <summary>
    /// Maps a <see cref="MaxioBillingException"/> onto an HTTP response: a provider 4xx is
    /// caller-actionable and is surfaced with its status; transport failures and unreadable
    /// provider responses carry no status and surface as a 502.
    /// </summary>
    internal static ObjectResult BillingProblem(MaxioBillingException ex)
    {
        var status = ex.StatusCode ?? StatusCodes.Status502BadGateway;
        return new ObjectResult(new
        {
            title = "Subscription billing error",
            status,
            detail = ex.Message
        })
        {
            StatusCode = status
        };
    }
}
