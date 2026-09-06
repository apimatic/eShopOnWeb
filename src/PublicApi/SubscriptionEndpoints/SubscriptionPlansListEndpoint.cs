using System;
using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;
using MaxioAdvancedBilling;
using MaxioAdvancedBilling.Core.ErrorResponse;
using MaxioAdvancedBilling.Core.Exceptions;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class SubscriptionPlansListEndpoint : IEndpoint<IResult>
{
    private readonly MaxioAdvancedBillingClient _maxioClient;
    private readonly IConfiguration _configuration;
    private readonly IHttpContextAccessor _httpContextAccessor;
    private readonly ILogger<SubscriptionPlansListEndpoint> _logger;

    public SubscriptionPlansListEndpoint(
        MaxioAdvancedBillingClient maxioClient,
        IConfiguration configuration,
        IHttpContextAccessor httpContextAccessor,
        ILogger<SubscriptionPlansListEndpoint> logger)
    {
        _maxioClient = maxioClient;
        _configuration = configuration;
        _httpContextAccessor = httpContextAccessor;
        _logger = logger;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/subscription-plans", HandleAsync)
           .Produces<SubscriptionPlansListResponse>()
           .WithTags("SubscriptionEndpoints")
           .RequireAuthorization();
    }

    public async Task<IResult> HandleAsync()
    {
        var response = new SubscriptionPlansListResponse();
        var httpContext = _httpContextAccessor.HttpContext;

        if (httpContext == null)
        {
            return Results.Unauthorized();
        }

        var userId = httpContext.User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        if (string.IsNullOrEmpty(userId))
        {
            _logger.LogWarning("No user ID found in JWT claims");
            return Results.Unauthorized();
        }

        try
        {
            var productFamilyHandle = _configuration["Maxio:ProductFamilyHandle"] ?? "eshop-subscribe";

            var products = await _maxioClient.Products.ListProducts(
                dateField: null, filter: null, endDate: null, endDatetime: null,
                startDate: null, startDatetime: null, includeArchived: null, include: null,
                page: 1, perPage: 200, ct: default);

            var subscribablePlans = products
                .Where(pr => pr.Product?.ProductFamily?.Handle == productFamilyHandle)
                .Select(pr => pr.Product)
                .Where(p => p != null)
                .Select(p => new SubscriptionPlanDto
                {
                    Id = p?.Id,
                    Name = p?.Name,
                    Handle = p?.Handle,
                    PriceInCents = p?.PriceInCents,
                    BillingInterval = p?.Interval,
                    BillingIntervalUnit = p?.IntervalUnit?.Value
                })
                .ToList();

            response.Plans.AddRange(subscribablePlans);
            return Results.Ok(response);
        }
        catch (SdkException<RawError> ex)
        {
            _logger.LogError(ex, "Error listing products from Maxio. Status: {Status}",
                (int?)ex.Error.StatusCode);
            return Results.StatusCode(500);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected error listing subscription plans");
            return Results.StatusCode(500);
        }
    }
}
