using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.PublicApi.Maxio;
using Microsoft.eShopWeb.PublicApi.Subscriptions;
using Microsoft.Extensions.Logging;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// GET /api/subscription-plans — lists the subscription plans a logged-in shopper can subscribe to.
/// </summary>
public class ListSubscriptionPlansEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/subscription-plans",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
            async (ISubscriptionService subscriptions,
                ILogger<ListSubscriptionPlansEndpoint> logger,
                CancellationToken cancellationToken) =>
            {
                try
                {
                    var plans = await subscriptions.ListAvailablePlansAsync(cancellationToken).ConfigureAwait(false);
                    var currency = await subscriptions.GetSiteCurrencyAsync(cancellationToken).ConfigureAwait(false);

                    var response = new SubscriptionPlanListResponse
                    {
                        Plans = plans
                            .Where(p => p.ArchivedAt is null)
                            .Select(p => new SubscriptionPlanDto
                            {
                                Id = p.Id,
                                Handle = p.Handle ?? string.Empty,
                                Name = p.Name ?? string.Empty,
                                Description = p.Description,
                                PriceInCents = p.PriceInCents ?? 0,
                                Currency = currency,
                                Interval = p.Interval ?? 0,
                                IntervalUnit = p.IntervalUnit ?? string.Empty,
                                PricePointId = p.ProductPricePointId ?? p.DefaultProductPricePointId,
                                RequiresPaymentMethod = p.RequireCreditCard ?? false,
                            })
                            .OrderBy(p => p.PriceInCents)
                            .ToList(),
                    };

                    return Results.Ok(response);
                }
                catch (System.Exception exception) when (exception is MaxioApiException or MaxioUnavailableException)
                {
                    return SubscriptionEndpointSupport.MapMaxioFailure(exception, logger);
                }
            })
            .Produces<SubscriptionPlanListResponse>()
            .WithTags("SubscriptionEndpoints");
    }
}
