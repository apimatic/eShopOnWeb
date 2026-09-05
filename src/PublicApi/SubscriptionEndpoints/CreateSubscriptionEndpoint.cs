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

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// Subscribes the calling shopper to a plan. Ensures a Maxio customer exists for them and
/// enrolls them, idempotently: submitting the same plan twice (e.g. a double-click) returns
/// the one subscription already on file instead of creating a duplicate.
/// </summary>
public class CreateSubscriptionEndpoint : IEndpoint<IResult, CreateSubscriptionRequest, ISubscriptionEnrollmentService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/subscriptions",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (CreateSubscriptionRequest request, ClaimsPrincipal user, UserManager<ApplicationUser> userManager, ISubscriptionEnrollmentService enrollmentService) =>
            {
                var buyer = await BuyerResolver.ResolveAsync(user, userManager);
                if (buyer is null)
                {
                    return Results.Unauthorized();
                }

                request.Buyer = buyer;
                return await HandleAsync(request, enrollmentService);
            })
            .Produces<CreateSubscriptionResponse>()
            .WithTags("SubscriptionEndpoints");
    }

    public async Task<IResult> HandleAsync(CreateSubscriptionRequest request, ISubscriptionEnrollmentService enrollmentService)
    {
        if (string.IsNullOrWhiteSpace(request.PlanHandle))
        {
            return Results.BadRequest("planHandle is required.");
        }

        var response = new CreateSubscriptionResponse(request.CorrelationId());

        var (subscription, alreadyExisted) = await enrollmentService.SubscribeAsync(request.Buyer, request.PlanHandle);
        response.Subscription = SubscriptionMapper.ToDto(subscription);
        response.AlreadyExisted = alreadyExisted;

        return alreadyExisted ? Results.Ok(response) : Results.Created("api/my-subscriptions", response);
    }
}
