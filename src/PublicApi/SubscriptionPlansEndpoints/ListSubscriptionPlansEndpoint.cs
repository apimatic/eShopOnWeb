using System;
using System.Linq;
using System.Threading.Tasks;
using AutoMapper;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using MaxioAdvancedBilling;
using MaxioAdvancedBilling.Core.ErrorResponse;
using MaxioAdvancedBilling.Core.Exceptions;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionPlansEndpoints;

/// <summary>
/// List available subscription plans
/// </summary>
public class ListSubscriptionPlansEndpoint : IEndpoint<IResult, MaxioAdvancedBillingClient>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/subscription-plans",
            async (MaxioAdvancedBillingClient client) =>
            {
                return await HandleAsync(client);
            })
           .Produces<ListSubscriptionPlansResponse>()
           .WithTags("SubscriptionPlansEndpoints")
           .WithName("ListSubscriptionPlans");
    }

    public async Task<IResult> HandleAsync(MaxioAdvancedBillingClient client)
    {
        var response = new ListSubscriptionPlansResponse();

        try
        {
            var products = await client.Products.ListProducts(
                dateField: null,
                filter: null,
                endDate: null,
                endDatetime: null,
                startDate: null,
                startDatetime: null,
                includeArchived: false,
                include: null,
                page: 1,
                perPage: 100,
                ct: default);

            if (products != null)
            {
                foreach (var productResponse in products)
                {
                    if (productResponse?.Product != null)
                    {
                        var plan = new SubscriptionPlanDto
                        {
                            Id = productResponse.Product.Id ?? 0,
                            Name = productResponse.Product.Name ?? string.Empty,
                            Handle = productResponse.Product.Handle ?? string.Empty,
                            Description = productResponse.Product.Description,
                            PriceInCents = productResponse.Product.PriceInCents ?? 0,
                            Interval = productResponse.Product.Interval ?? 1,
                            IntervalUnit = productResponse.Product.IntervalUnit?.Value ?? "month"
                        };
                        response.Plans.Add(plan);
                    }
                }
            }

            return Results.Ok(response);
        }
        catch (SdkException<RawError> ex)
        {
            return Results.StatusCode((int?)ex.Error.StatusCode ?? 500);
        }
        catch (Exception ex)
        {
            return Results.StatusCode(500);
        }
    }
}
