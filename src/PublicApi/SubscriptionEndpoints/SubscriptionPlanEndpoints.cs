using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using MaxioAdvancedBilling;
using MaxioAdvancedBilling.Core.Exceptions;
using MaxioAdvancedBilling.Core.ErrorResponse;
using MaxioAdvancedBilling.Models;
using MaxioAdvancedBilling.Errors;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public static class SubscriptionPlanEndpoints
{
    public static void AddSubscriptionPlanRoutes(this IEndpointRouteBuilder app)
    {
        app.MapGet("api/subscription-plans", async (
            MaxioAdvancedBillingClient client,
            IConfiguration configuration,
            CancellationToken ct) =>
        {
            var response = new ListPlanResponse();
            var familyHandle = configuration["Maxio:ProductFamilyHandle"] ?? "";
            var productFamilyId = familyHandle.StartsWith("handle:") ? familyHandle : $"handle:{familyHandle}";

            try
            {
                var products = await client.ProductFamilies.ListProductsForProductFamily(
                    productFamilyId: productFamilyId,
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
                    ct: ct);

                foreach (var productResponse in products)
                {
                    var product = productResponse.Product;
                    if (product == null) continue;

                    response.Plans.Add(new SubscriptionPlanDto
                    {
                        Id = product.Id ?? 0,
                        Name = product.Name ?? "",
                        Handle = product.Handle ?? "",
                        Description = product.Description ?? "",
                        PriceInCents = product.PriceInCents ?? 0,
                        Interval = product.Interval ?? 0,
                        IntervalUnit = product.IntervalUnit?.Value ?? ""
                    });
                }
            }
            catch (SdkException<ListProductsForProductFamilyError> ex)
            {
                if (ex.Error.TryGetString(out var errorString))
                    return Results.Problem(errorString, statusCode: 400);
                if (ex.Error.TryGetRawError(out var rawError))
                    return Results.Problem(rawError.ReadAsString(), statusCode: (int)rawError.StatusCode);
                return Results.Problem("Failed to list subscription plans.", statusCode: 500);
            }
            catch (SdkException<RawError> ex)
            {
                return Results.Problem(ex.Error.ReadAsString(), statusCode: (int)ex.Error.StatusCode);
            }
            catch (JsonException ex)
            {
                return Results.Problem(
                    "The billing service returned an unexpected response. Please try again later.",
                    statusCode: 502);
            }

            return Results.Ok(response);
        })
        .WithName("ListSubscriptionPlans")
        .Produces<ListPlanResponse>()
        .WithTags("SubscriptionEndpoints");
    }
}

public class ListPlanResponse
{
    public List<SubscriptionPlanDto> Plans { get; set; } = new();
}

public class SubscriptionPlanDto
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public string Handle { get; set; } = "";
    public string Description { get; set; } = "";
    public long PriceInCents { get; set; }
    public int Interval { get; set; }
    public string IntervalUnit { get; set; } = "";
}
