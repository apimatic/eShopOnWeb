using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.PublicApi.Maxio;
using Microsoft.Extensions.Options;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionPlanEndpoints;

/// <summary>
/// Lists the subscription plans available for signup, sourced from the
/// configured Maxio product family.
/// </summary>
public class ListSubscriptionPlansEndpoint : IEndpoint<IResult, IMaxioClient, IOptions<MaxioSettings>>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/subscription-plans",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (IMaxioClient maxioClient, IOptions<MaxioSettings> maxioSettings) =>
            {
                return await HandleAsync(maxioClient, maxioSettings);
            })
            .Produces<ListSubscriptionPlansResponse>()
            .WithTags("SubscriptionPlanEndpoints");
    }

    public async Task<IResult> HandleAsync(IMaxioClient maxioClient, IOptions<MaxioSettings> maxioSettings)
    {
        var response = new ListSubscriptionPlansResponse();

        var family = await maxioClient.GetProductFamilyByHandleAsync(maxioSettings.Value.ProductFamilyHandle);
        if (family == null)
        {
            return Results.Problem($"No Maxio product family found with handle '{maxioSettings.Value.ProductFamilyHandle}'.");
        }

        var products = await maxioClient.GetProductsByFamilyAsync(family.Id);

        response.Plans.AddRange(products
            .Where(p => p.ArchivedAt == null)
            .Select(p => new SubscriptionPlanDto
            {
                Id = p.Id,
                Name = p.Name ?? string.Empty,
                Handle = p.Handle ?? string.Empty,
                Description = p.Description,
                PriceInCents = p.PriceInCents,
                Interval = p.Interval,
                IntervalUnit = p.IntervalUnit ?? string.Empty
            }));

        return Results.Ok(response);
    }
}
