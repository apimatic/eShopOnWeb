using System;
using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;
using MaxioAdvancedBilling;
using MaxioAdvancedBilling.Core.Exceptions;
using MaxioAdvancedBilling.Core.ErrorResponse;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class ListSubscriptionPlansEndpoint : IEndpoint<IResult, ListPlansRequest>
{
    private readonly MaxioAdvancedBillingClient _maxioClient;
    private readonly MaxioSettings _settings;
    private readonly ILogger<ListSubscriptionPlansEndpoint> _logger;

    public ListSubscriptionPlansEndpoint(
        MaxioAdvancedBillingClient maxioClient,
        IOptions<MaxioSettings> settings,
        ILogger<ListSubscriptionPlansEndpoint> logger)
    {
        _maxioClient = maxioClient;
        _settings = settings.Value;
        _logger = logger;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/subscription-plans",
            async (ClaimsPrincipal user) =>
            {
                var request = new ListPlansRequest(user.FindFirst(ClaimTypes.NameIdentifier)?.Value);
                return await HandleAsync(request);
            })
            .RequireAuthorization()
            .Produces<ListPlansResponse>();
    }

    public async Task<IResult> HandleAsync(ListPlansRequest request)
    {
        var response = new ListPlansResponse(request.CorrelationId());

        try
        {
            if (string.IsNullOrEmpty(_settings.ProductFamilyHandle))
            {
                _logger.LogWarning("ProductFamilyHandle not configured");
                return Results.StatusCode(500);
            }

            var plans = await _maxioClient.ProductFamilies.ListProductsForProductFamily(
                productFamilyId: _settings.ProductFamilyHandle,
                dateField: null,
                filter: null,
                startDate: null,
                endDate: null,
                startDatetime: null,
                endDatetime: null,
                includeArchived: null,
                include: null,
                page: 1,
                perPage: 100,
                ct: default);

            if (plans != null)
            {
                foreach (var product in plans)
                {
                    if (product.Product != null)
                    {
                        response.Plans.Add(new SubscriptionPlanDto
                        {
                            Id = product.Product.Id,
                            Name = product.Product.Name,
                            Handle = product.Product.Handle,
                            PriceInCents = product.Product.PriceInCents,
                            Interval = product.Product.Interval,
                            IntervalUnit = product.Product.IntervalUnit?.ToString(),
                            CreatedAt = product.Product.CreatedAt,
                            UpdatedAt = product.Product.UpdatedAt
                        });
                    }
                }
            }

            return Results.Ok(response);
        }
        catch (SdkException<RawError> ex)
        {
            _logger.LogError(ex, "Failed to fetch subscription plans from Maxio: HTTP {StatusCode}",
                (int)ex.Error.StatusCode);
            return Results.StatusCode((int)ex.Error.StatusCode);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected error fetching subscription plans");
            return Results.StatusCode(500);
        }
    }
}
