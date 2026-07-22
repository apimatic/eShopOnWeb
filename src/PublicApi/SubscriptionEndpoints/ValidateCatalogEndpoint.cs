using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// Verifies that the billing provider's catalog matches this integration's configuration — the
/// product family, both plan handles, and that the usage component really is metered. This is the
/// operator's read-back check for the provider seed (UC0, step 6) and the standing check behind
/// UC2's preconditions.
/// <para>
/// Always returns 200 with a report rather than failing: an invalid catalog is information the
/// operator needs, not a server error.
/// </para>
/// </summary>
public class ValidateCatalogEndpoint : IEndpoint<IResult, IBillingClient>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/subscription-catalog/validation",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS, AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (IBillingClient billingClient) => await HandleAsync(billingClient))
            .Produces<ValidateCatalogResponse>()
            .WithTags("SubscriptionEndpoints");
    }

    public async Task<IResult> HandleAsync(IBillingClient billingClient)
    {
        var validation = await billingClient.ValidateCatalogAsync();

        return Results.Ok(new ValidateCatalogResponse
        {
            IsValid = validation.IsValid,
            ProductFamilyHandle = validation.ProductFamilyHandle,
            ProductFamilyId = validation.ProductFamilyId,
            IsMeteredComponentValid = validation.IsMeteredComponentValid,
            MeteredComponentId = validation.MeteredComponentId,
            MeteredComponentKind = validation.MeteredComponentKind,
            Errors = validation.Errors.ToList(),
            Plans = validation.Plans.Select(plan => plan.ToDto()).ToList()
        });
    }
}
