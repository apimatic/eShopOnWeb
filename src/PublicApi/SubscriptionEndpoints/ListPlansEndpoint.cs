using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using MaxioAdvancedBilling;
using MaxioAdvancedBilling.Core.ErrorResponse;
using MaxioAdvancedBilling.Core.Exceptions;
using MaxioAdvancedBilling.Models.Enums;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// List available subscription plans
/// </summary>
public class ListPlansEndpoint : IEndpoint<IResult>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/subscription-plans",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async (MaxioAdvancedBillingClient maxioClient, ILogger<ListPlansEndpoint> logger, IConfiguration configuration) =>
            {
                return await HandleAsync(maxioClient, logger, configuration);
            })
            .Produces<ListPlansResponse>()
            .WithName("ListSubscriptionPlans")
            .WithTags("SubscriptionEndpoints");
    }

    public async Task<IResult> HandleAsync()
    {
        throw new NotImplementedException();
    }

    private async Task<IResult> HandleAsync(MaxioAdvancedBillingClient maxioClient, ILogger<ListPlansEndpoint> logger, IConfiguration configuration)
    {
        var response = new ListPlansResponse(Guid.NewGuid());
        var productFamilyHandle = configuration["Maxio:ProductFamilyHandle"] ?? "eshop-subscribe";

        try
        {
            var products = await maxioClient.Products.ListProducts(
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
                if (productResponse.Product != null)
                {
                    var plan = new PlanDto
                    {
                        Id = productResponse.Product.Id ?? 0,
                        Name = productResponse.Product.Name ?? string.Empty,
                        Handle = productResponse.Product.Handle ?? string.Empty,
                        PriceInCents = productResponse.Product.PriceInCents ?? 0,
                        Description = productResponse.Product.Description ?? string.Empty,
                        IntervalInMonths = productResponse.Product.Interval ?? 1
                    };
                    response.Plans.Add(plan);
                }
            }

            return Results.Ok(response);
        }
        catch (SdkException<RawError> ex)
        {
            logger.LogError($"Error listing products: HTTP {(int)ex.Error.StatusCode}");
            return Results.BadRequest(new { error = "Failed to list subscription plans" });
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Unexpected error listing subscription plans");
            return Results.StatusCode(500);
        }
    }
}
