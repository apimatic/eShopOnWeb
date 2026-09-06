using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb;
using Microsoft.eShopWeb.Infrastructure.Services;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class ListSubscriptionPlansEndpoint : IEndpoint<IResult, EmptyRequest, MaxioSettings>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/subscription-plans",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
            async (MaxioSettings maxioSettings, IMaxioSubscriptionService subscriptionService) =>
            {
                return await HandleAsync(new EmptyRequest(), maxioSettings, subscriptionService);
            })
            .Produces<ListSubscriptionPlansResponse>()
            .WithTags("SubscriptionEndpoints");
    }

    public async Task<IResult> HandleAsync(EmptyRequest request, MaxioSettings maxioSettings)
    {
        await Task.Delay(10);
        throw new NotImplementedException("Use the overload with IMaxioSubscriptionService");
    }

    private async Task<IResult> HandleAsync(EmptyRequest request, MaxioSettings maxioSettings, IMaxioSubscriptionService subscriptionService)
    {
        await Task.Delay(10);
        try
        {
            if (maxioSettings.ProductFamilyId <= 0)
            {
                return Results.BadRequest(new { error = "Maxio product family ID not configured" });
            }

            var response = new ListSubscriptionPlansResponse(request.CorrelationId());

            var products = await subscriptionService.GetProductsAsync(maxioSettings.ProductFamilyId);
            if (products?.Products != null)
            {
                foreach (var product in products.Products)
                {
                    response.Plans.Add(new SubscriptionPlanDto
                    {
                        Id = product.Id,
                        Name = product.Name,
                        Handle = product.Handle,
                        Description = product.Description,
                        Price = product.PriceInCents / 100m,
                        BillingInterval = $"Every {product.Interval} {product.IntervalUnit}"
                    });
                }
            }

            return Results.Ok(response);
        }
        catch (Exception ex)
        {
            return Results.BadRequest(new { error = ex.Message });
        }
    }
}

public class EmptyRequest : BaseRequest
{
}

public class ListSubscriptionPlansResponse : BaseResponse
{
    public ListSubscriptionPlansResponse()
    {
    }

    public ListSubscriptionPlansResponse(Guid correlationId) : base(correlationId)
    {
    }

    public List<SubscriptionPlanDto> Plans { get; } = new();
}
