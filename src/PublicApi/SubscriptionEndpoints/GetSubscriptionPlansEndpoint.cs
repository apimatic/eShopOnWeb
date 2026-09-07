using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using MaxioAdvancedBilling;
using MaxioAdvancedBilling.Core.Exceptions;
using MaxioAdvancedBilling.Core.ErrorResponse;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class GetSubscriptionPlansEndpoint : IEndpoint<IResult, GetSubscriptionPlansRequestDto>
{
    private IConfiguration? Configuration { get; set; }
    private MaxioAdvancedBillingClient? MaxioClient { get; set; }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/subscription-plans",
            async (IConfiguration config, MaxioAdvancedBillingClient maxioClient) =>
            {
                var endpoint = new GetSubscriptionPlansEndpoint
                {
                    Configuration = config,
                    MaxioClient = maxioClient
                };
                return await endpoint.HandleAsync(new GetSubscriptionPlansRequestDto());
            })
            .Produces<GetSubscriptionPlansResponse>()
            .WithTags("SubscriptionEndpoints")
            .WithName("GetSubscriptionPlans");
    }

    public async Task<IResult> HandleAsync(GetSubscriptionPlansRequestDto request)
    {
        var response = new GetSubscriptionPlansResponse(request.CorrelationId());

        try
        {
            var productFamilyHandle = Configuration!["Maxio:ProductFamilyHandle"] ?? "eshop-subscribe";

            var products = await MaxioClient!.Products.ListProducts(
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

            foreach (var productResponse in products)
            {
                if (productResponse.Product?.ProductFamily?.Handle == productFamilyHandle)
                {
                    response.Plans.Add(new SubscriptionPlanDto
                    {
                        Handle = productResponse.Product.Handle ?? string.Empty,
                        Name = productResponse.Product.Name ?? string.Empty,
                        Description = productResponse.Product.Description ?? string.Empty,
                        PriceInCents = productResponse.Product?.PriceInCents ?? 0
                    });
                }
            }

            return Results.Ok(response);
        }
        catch (SdkException<RawError> ex)
        {
            return Results.StatusCode((int?)ex.Error.StatusCode ?? 500);
        }
        catch (Exception)
        {
            return Results.StatusCode(500);
        }
    }
}

public class GetSubscriptionPlansRequestDto : BaseRequest
{
}

public class GetSubscriptionPlansResponse : BaseResponse
{
    public GetSubscriptionPlansResponse(Guid correlationId)
    {
        _correlationId = correlationId;
    }

    public List<SubscriptionPlanDto> Plans { get; set; } = new();
}

public class SubscriptionPlanDto
{
    public string Handle { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public long PriceInCents { get; set; }
}
