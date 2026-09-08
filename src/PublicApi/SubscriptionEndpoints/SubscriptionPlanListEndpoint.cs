using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints.Maxio;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// Lists the subscription plans available to shoppers. Plans are read live
/// from the Maxio product family configured under Maxio:ProductFamilyHandle.
/// </summary>
public class SubscriptionPlanListEndpoint : IEndpoint<IResult, SubscriptionPlanListRequest, IMaxioBillingClient>
{
    private readonly MaxioOptions _maxioOptions;

    public SubscriptionPlanListEndpoint(IMaxioBillingClient maxioClient, Microsoft.Extensions.Options.IOptions<MaxioOptions> maxioOptions)
    {
        _maxioClient = maxioClient;
        _maxioOptions = maxioOptions.Value;
    }

    private IMaxioBillingClient _maxioClient { get; }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/subscription-plans",
            async (IMaxioBillingClient maxioClient) =>
            {
                return await HandleAsync(new SubscriptionPlanListRequest(), maxioClient);
            })
            .Produces<SubscriptionPlanListResponse>()
            .RequireAuthorization(MaxioEndpointResults.JwtAuthorize())
            .WithTags("SubscriptionEndpoints");
    }

    public async Task<IResult> HandleAsync(SubscriptionPlanListRequest request, IMaxioBillingClient maxioClient)
    {
        var response = new SubscriptionPlanListResponse(request.CorrelationId())
        {
            ProductFamilyHandle = _maxioOptions.ProductFamilyHandle
        };

        try
        {
            var products = await _maxioClient.ListProductsForFamilyAsync(_maxioOptions.ProductFamilyHandle, CancellationToken.None);

            response.Plans = products
                .Where(p => p.ArchivedAt is null && !string.IsNullOrEmpty(p.Handle))
                .OrderBy(p => p.PriceInCents ?? 0)
                .Select(p => new SubscriptionPlanDto
                {
                    Handle = p.Handle!,
                    Name = p.Name,
                    Description = p.Description,
                    PriceInCents = p.PriceInCents ?? 0,
                    Interval = p.Interval,
                    IntervalUnit = p.IntervalUnit,
                    TrialInterval = p.TrialInterval,
                    TrialIntervalUnit = p.TrialIntervalUnit,
                    RequireCreditCard = p.RequireCreditCard ?? false
                })
                .ToList();

            return Results.Ok(response);
        }
        catch (MaxioApiException ex)
        {
            return MaxioEndpointResults.FromMaxioException(ex);
        }
    }
}
