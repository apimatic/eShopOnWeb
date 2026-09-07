using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Threading.Tasks;
using MaxioAdvancedBilling;
using MaxioAdvancedBilling.Core.Exceptions;
using MaxioAdvancedBilling.Core.ErrorResponse;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class ListSubscriptionPlansEndpoint : IEndpoint<IResult>
{
    private readonly MaxioAdvancedBillingClient _maxioClient;
    private readonly MaxioSettings _maxioSettings;

    public ListSubscriptionPlansEndpoint(MaxioAdvancedBillingClient maxioClient, Microsoft.Extensions.Options.IOptions<MaxioSettings> maxioSettings)
    {
        _maxioClient = maxioClient;
        _maxioSettings = maxioSettings.Value;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/subscription-plans",
            async () => await HandleAsync())
            .WithName("GetSubscriptionPlans")
            .Produces<ListSubscriptionPlansResponse>()
            .WithTags("SubscriptionEndpoints")
            .AllowAnonymous()
            .WithName("GetSubscriptionPlans");
    }

    public async Task<IResult> HandleAsync()
    {
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
                ct: default);

            var plans = response
                .Where(p => p.Product?.ProductFamily?.Handle == _maxioSettings.ProductFamilyHandle)
                .Select(p => new SubscriptionPlanDto
                {
                    Id = p.Product?.Id ?? 0,
                    Handle = p.Product?.Handle ?? string.Empty,
                    Name = p.Product?.Name ?? string.Empty,
                    PriceInCents = p.Product?.PriceInCents ?? 0,
                    Interval = p.Product?.Interval ?? 0,
                    IntervalUnit = p.Product?.IntervalUnit?.ToString() ?? string.Empty
                })
                .ToList();

            return Results.Ok(new ListSubscriptionPlansResponse { Plans = plans });
        }
        catch (SdkException<RawError> ex)
        {
            int statusCode = ex.Error.StatusCode != null ? (int)ex.Error.StatusCode : (int)HttpStatusCode.InternalServerError;
            return Results.StatusCode(statusCode);
        }
    }
}

public class ListSubscriptionPlansResponse : BaseResponse
{
    public List<SubscriptionPlanDto> Plans { get; set; } = new();
}
