using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class ListSubscriptionPlansEndpoint : IEndpoint<IResult, MaxioApiClient>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/subscription-plans",
            async (MaxioApiClient maxioClient) => await HandleAsync(maxioClient))
            .Produces<ListSubscriptionPlansResponse>()
            .WithTags("SubscriptionEndpoints");
    }

    public async Task<IResult> HandleAsync(MaxioApiClient maxioClient)
    {
        var request = new ListSubscriptionPlansRequest();
        var response = new ListSubscriptionPlansResponse(request.CorrelationId());

        try
        {
            if (maxioClient != null)
            {
                var productsResponse = await maxioClient.GetAsync<ProductsListResponse>("/products.json");
                if (productsResponse != null)
                {
                    response.Plans.AddRange(productsResponse.Select(p => p.Product).OfType<ProductDetailsDto>().Select(MapToSubscriptionPlanDto));
                }
            }
            else
            {
                response.Error = "Maxio client is not configured";
            }
        }
        catch (Exception ex)
        {
            response.Error = $"Failed to retrieve subscription plans: {ex.Message}";
            return Results.BadRequest(response);
        }

        return Results.Ok(response);
    }

    private SubscriptionPlanDto MapToSubscriptionPlanDto(ProductDetailsDto product)
    {
        return new SubscriptionPlanDto
        {
            Id = product.Id,
            Name = product.Name,
            Handle = product.Handle,
            Description = product.Description,
            PriceInCents = product.PriceInCents,
            Interval = product.Interval,
            IntervalUnit = product.IntervalUnit,
            AccountingCode = product.AccountingCode,
            RequireCreditCard = product.RequireCreditCard ?? false,
            RequireBillingAddress = product.RequireBillingAddress ?? false,
            TrialPriceInCents = product.TrialPriceInCents,
            TrialInterval = product.TrialInterval
        };
    }
}

public class ListSubscriptionPlansRequest : BaseRequest
{
}

public class ListSubscriptionPlansResponse : BaseResponse
{
    public ListSubscriptionPlansResponse(Guid correlationId) : base(correlationId)
    {
    }

    public ListSubscriptionPlansResponse()
    {
    }

    public List<SubscriptionPlanDto> Plans { get; set; } = new List<SubscriptionPlanDto>();
    public string? Error { get; set; }
}

// Maxio returns an array of product wrappers directly
public class ProductWrapper
{
    [JsonPropertyName("product")]
    public ProductDetailsDto? Product { get; set; }
}

// For List response, we'll deserialize to List<ProductWrapper> directly
public class ProductsListResponse : List<ProductWrapper>
{
}
