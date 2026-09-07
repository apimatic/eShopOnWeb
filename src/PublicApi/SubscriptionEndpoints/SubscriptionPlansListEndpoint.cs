using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using MaxioAdvancedBilling;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using MinimalApi.Endpoint;
using Swashbuckle.AspNetCore.Annotations;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// List available subscription plans
/// </summary>
public partial class SubscriptionPlansListEndpoint : IEndpoint<IResult, EmptyRequest, MaxioAdvancedBillingClient>
{
    private readonly IConfiguration _configuration;

    public SubscriptionPlansListEndpoint(IConfiguration configuration)
    {
        _configuration = configuration;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/subscription-plans",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (MaxioAdvancedBillingClient maxioClient) =>
            {
                return await HandleAsync(new EmptyRequest(), maxioClient);
            })
            .Produces<ListPlansResponse>()
            .WithTags("SubscriptionEndpoints")
            .WithName("ListSubscriptionPlans");
    }

    public async Task<IResult> HandleAsync(EmptyRequest request, MaxioAdvancedBillingClient maxioClient)
    {
        var response = new ListPlansResponse(request.CorrelationId());

        try
        {
            var productFamilyHandle = _configuration["Maxio:ProductFamilyHandle"] ?? "eshop-subscribe";

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
                if (productResponse.Product == null)
                    continue;

                response.Plans.Add(new SubscriptionPlanDto
                {
                    Id = (int)(productResponse.Product.Id ?? 0),
                    Name = productResponse.Product.Name,
                    Handle = productResponse.Product.Handle,
                    Price = (decimal?)productResponse.Product.PriceInCents / 100m ?? 0m,
                    Interval = (int)(productResponse.Product.Interval ?? 1),
                    IntervalUnit = productResponse.Product.IntervalUnit?.ToString()
                });
            }

            return Results.Ok(response);
        }
        catch (System.Text.Json.JsonException ex)
        {
            return Results.BadRequest(new { error = "Failed to parse subscription plans response", details = ex.Message });
        }
        catch (System.Net.Http.HttpRequestException ex)
        {
            return Results.StatusCode(503);
        }
        catch (Exception ex)
        {
            return Results.StatusCode(500);
        }
    }

    public class ListPlansResponse : BaseResponse
    {
        public ListPlansResponse()
        {
        }

        public ListPlansResponse(System.Guid correlationId) : base(correlationId)
        {
        }

        public System.Collections.Generic.List<SubscriptionPlanDto> Plans { get; } = new();
    }
}
