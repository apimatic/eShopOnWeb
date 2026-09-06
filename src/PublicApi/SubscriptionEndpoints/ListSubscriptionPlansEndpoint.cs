using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using MaxioAdvancedBilling;
using MaxioAdvancedBilling.Core.ErrorResponse;
using MaxioAdvancedBilling.Core.Exceptions;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// Lists available subscription plans
/// </summary>
public class ListSubscriptionPlansEndpoint : IEndpoint<IResult, EmptyRequest, AdvancedBillingClient>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/subscription-plans",
            async (AdvancedBillingClient client, CancellationToken ct) =>
            {
                return await HandleAsync(new EmptyRequest(), client, ct);
            })
            .Produces<ListSubscriptionPlansResponse>()
            .WithTags("SubscriptionEndpoints");
    }

    public async Task<IResult> HandleAsync(EmptyRequest request, AdvancedBillingClient client, CancellationToken ct = default)
    {
        var response = new ListSubscriptionPlansResponse(Guid.NewGuid());

        try
        {
            // List products with pagination defaults: page=1, perPage=20
            var products = await client.Products.ListProducts(
                null, null, null, null, null, null, null, null, 1, 20, ct);

            foreach (var productResponse in products)
            {
                if (productResponse.Product != null)
                {
                    response.Plans.Add(new SubscriptionPlanDto
                    {
                        Id = productResponse.Product.Id ?? 0,
                        Handle = productResponse.Product.Handle ?? string.Empty,
                        Name = productResponse.Product.Name ?? string.Empty,
                        Description = productResponse.Product.Description,
                        PriceInCents = productResponse.Product.PriceInCents ?? 0,
                        Interval = productResponse.Product.Interval,
                        IntervalUnit = productResponse.Product.IntervalUnit?.Value
                    });
                }
            }

            return Results.Ok(response);
        }
        catch (SdkException<RawError> ex)
        {
            return Results.StatusCode((int)(ex.Error.StatusCode ?? System.Net.HttpStatusCode.InternalServerError));
        }
        catch (JsonException)
        {
            return Results.StatusCode(500);
        }
        catch (Exception ex)
        {
            return Results.StatusCode(500);
        }
    }
}

public class EmptyRequest : BaseRequest
{
}

public class ListSubscriptionPlansResponse : BaseResponse
{
    public ListSubscriptionPlansResponse(Guid correlationId) : base(correlationId)
    {
    }

    public List<SubscriptionPlanDto> Plans { get; set; } = new();
}
