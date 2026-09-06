using System;
using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class CreateSubscriptionEndpoint : IEndpoint<IResult, CreateSubscriptionRequestDto, MaxioSubscriptionService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/subscriptions",
            async (HttpContext context, CreateSubscriptionRequestDto request, MaxioSubscriptionService subscriptionService) =>
            {
                var userReference = context.User?.FindFirst(ClaimTypes.NameIdentifier)?.Value
                                   ?? context.User?.FindFirst("sub")?.Value
                                   ?? context.User?.FindFirst(ClaimTypes.Name)?.Value;

                if (string.IsNullOrEmpty(userReference))
                {
                    return Results.Unauthorized();
                }

                return await HandleAsync(request, subscriptionService, userReference);
            })
            .RequireAuthorization()
            .Produces<CreateSubscriptionResponse>()
            .WithTags("SubscriptionEndpoints")
            .WithName("CreateSubscription");
    }

    async Task<IResult> IEndpoint<IResult, CreateSubscriptionRequestDto, MaxioSubscriptionService>.HandleAsync(CreateSubscriptionRequestDto request, MaxioSubscriptionService subscriptionService)
    {
        // This interface implementation is provided for framework compatibility.
        // The real implementation is called from AddRoute with the user reference.
        throw new NotImplementedException("Must be called through the route handler that provides user context");
    }

    public async Task<IResult> HandleAsync(CreateSubscriptionRequestDto request, MaxioSubscriptionService subscriptionService, string userReference = "")
    {
        var response = new CreateSubscriptionResponse();

        try
        {
            var (created, subscription) = await subscriptionService.CreateOrGetSubscriptionAsync(
                userReference,
                request.ProductId,
                request.ProductHandle,
                System.Threading.CancellationToken.None);

            response.Created = created;
            response.Subscription = subscription;

            return created ? Results.Created($"api/subscriptions/{subscription.Id}", response) : Results.Ok(response);
        }
        catch (Exception ex)
        {
            return Results.BadRequest(new { error = ex.Message });
        }
    }
}
