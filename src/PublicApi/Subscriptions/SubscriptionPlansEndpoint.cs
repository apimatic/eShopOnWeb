using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.Infrastructure.Identity;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.Subscriptions;

/// <summary>
/// Lists the active subscription plans from Maxio Advanced Billing.
/// </summary>
public sealed class SubscriptionPlansEndpoint : IEndpoint<IResult>
{
    private readonly IMaxioBillingClient _billingClient;

    public SubscriptionPlansEndpoint(IMaxioBillingClient billingClient)
    {
        _billingClient = billingClient;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/subscription-plans",
            [Microsoft.AspNetCore.Authorization.Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
            async (CancellationToken cancellationToken) => await HandleAsync(cancellationToken))
            .Produces<SubscriptionPlansResponse>()
            .WithTags("SubscriptionEndpoints");
    }

    public async Task<IResult> HandleAsync(CancellationToken cancellationToken)
    {
        var response = new SubscriptionPlansResponse(System.Guid.NewGuid());
        var products = await _billingClient.ListProductsAsync(cancellationToken);
        response.Plans.AddRange(products.Select(SubscriptionPlanDto.From));
        return Results.Ok(response);
    }

    public Task<IResult> HandleAsync() => HandleAsync(CancellationToken.None);
}
