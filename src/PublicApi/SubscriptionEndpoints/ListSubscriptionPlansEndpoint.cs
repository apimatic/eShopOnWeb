using System;
using System.Collections.Generic;
using System.Linq;
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

public class ListSubscriptionPlansEndpoint : IEndpoint<IResult, ListSubscriptionPlansRequest>
{
    private readonly MaxioAdvancedBillingClient _maxioClient;

    public ListSubscriptionPlansEndpoint(MaxioAdvancedBillingClient maxioClient)
    {
        _maxioClient = maxioClient;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/subscription-plans", HandleAsync)
            .RequireAuthorization()
            .Produces<ListSubscriptionPlansResponse>()
            .WithTags("SubscriptionEndpoints")
            .WithName("GetSubscriptionPlans");
    }

    public async Task<IResult> HandleAsync(ListSubscriptionPlansRequest request)
    {
        var response = new ListSubscriptionPlansResponse(request.CorrelationId());
        var ct = CancellationToken.None;

        try
        {
            var planHandles = new[] { "eshop-pro", "basic-plan" };

            foreach (var handle in planHandles)
            {
                try
                {
                    var productResponse = await _maxioClient.Products.ReadProductByHandle(handle, ct);
                    var product = productResponse.Product;

                    if (product != null)
                    {
                        response.Plans.Add(new SubscriptionPlanDto
                        {
                            Handle = product.Handle,
                            Name = product.Name,
                            Description = product.Description,
                            PriceInCents = product.PriceInCents,
                            Interval = product.Interval,
                            IntervalUnit = product.IntervalUnit?.Value
                        });
                    }
                }
                catch (SdkException<RawError> ex)
                {
                    if (ex.Error.StatusCode == System.Net.HttpStatusCode.NotFound)
                    {
                        continue;
                    }
                    throw;
                }
            }

            return Results.Ok(response);
        }
        catch
        {
            return Results.StatusCode(StatusCodes.Status500InternalServerError);
        }
    }
}
