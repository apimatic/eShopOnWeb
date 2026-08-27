using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Billing;
using Microsoft.eShopWeb.Infrastructure.Identity;
using Microsoft.Extensions.DependencyInjection;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public sealed class SubscribeEndpoint : IEndpoint<IResult, SubscribeRequest, HttpContext, CancellationToken>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/subscriptions",
                [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
                (SubscribeRequest request, HttpContext context, CancellationToken cancellationToken) =>
                    HandleAsync(request, context, cancellationToken))
            .Produces<SubscribeResponse>(StatusCodes.Status201Created)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .ProducesProblem(StatusCodes.Status422UnprocessableEntity)
            .ProducesProblem(StatusCodes.Status502BadGateway)
            .WithTags("SubscriptionEndpoints");
    }

    public async Task<IResult> HandleAsync(
        SubscribeRequest request,
        HttpContext context,
        CancellationToken cancellationToken)
    {
        var userManager = context.RequestServices.GetRequiredService<UserManager<ApplicationUser>>();
        var subscriptionService = context.RequestServices.GetRequiredService<ISubscriptionService>();
        var user = await BillingUserResolver.ResolveAsync(context, userManager);
        var subscription = await subscriptionService.SubscribeAsync(
            user,
            request.ProductHandle,
            cancellationToken);
        var response = new SubscribeResponse(SubscriptionDtoMapper.Map(subscription));
        return Results.Created($"api/my-subscriptions", response);
    }
}
