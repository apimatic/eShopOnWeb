using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.Infrastructure.Identity;
using MinimalApi.Endpoint;
using Swashbuckle.AspNetCore.Annotations;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// Lists the authenticated caller's subscriptions, straight from Maxio.
/// </summary>
public class ListMySubscriptionsEndpoint : IEndpoint<IResult, MySubscriptionsRequest, IMaxioBillingService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/my-subscriptions",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
            async (ClaimsPrincipal user, UserManager<ApplicationUser> userManager,
                IMaxioBillingService billingService) =>
            {
                return await SubscriptionResults.RunAsync(async () =>
                {
                    var identity = await SubscriberIdentity.ResolveAsync(user, userManager);
                    var request = new MySubscriptionsRequest { Reference = identity.Reference };
                    return await HandleAsync(request, billingService);
                });
            })
            .Produces<ListMySubscriptionsResponse>()
            .ProducesProblem(StatusCodes.Status502BadGateway)
            .WithTags("SubscriptionEndpoints")
            .WithMetadata(new SwaggerOperationAttribute("List my subscriptions",
                "Returns the authenticated caller's subscriptions as reported by Maxio."));
    }

    public async Task<IResult> HandleAsync(MySubscriptionsRequest request, IMaxioBillingService billingService)
    {
        var response = new ListMySubscriptionsResponse(request.CorrelationId());
        var subscriptions = await billingService.ListSubscriptionsAsync(request.Reference);
        response.Subscriptions.AddRange(subscriptions.Select(SubscriptionDto.FromDomain));
        return Results.Ok(response);
    }
}
