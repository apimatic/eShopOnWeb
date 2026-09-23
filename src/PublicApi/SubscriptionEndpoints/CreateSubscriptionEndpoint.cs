using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.Infrastructure.Identity;
using Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints.Maxio;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// Subscribes the authenticated user to a plan. Ensures a Maxio customer exists (idempotent) and
/// enrolls them; a double-submit returns the existing subscription rather than creating a second.
/// </summary>
public class CreateSubscriptionEndpoint : IEndpoint<IResult, SubscribeRequest, IMaxioSubscriptionService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/subscriptions",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
            async (SubscribeRequest request,
                   ClaimsPrincipal principal,
                   UserManager<ApplicationUser> userManager,
                   IMaxioSubscriptionService service,
                   CancellationToken ct) =>
            {
                var user = await MaxioUserResolver.ResolveAsync(principal, userManager);
                if (user is null)
                    return Results.Unauthorized();

                request ??= new SubscribeRequest();
                request.User = user;
                return await HandleAsync(request, service, ct);
            })
            .Produces<SubscribeResponse>(StatusCodes.Status201Created)
            .Produces<SubscribeResponse>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status502BadGateway)
            .WithTags("SubscriptionEndpoints");
    }

    public Task<IResult> HandleAsync(SubscribeRequest request, IMaxioSubscriptionService service) =>
        HandleAsync(request, service, CancellationToken.None);

    public async Task<IResult> HandleAsync(SubscribeRequest request, IMaxioSubscriptionService service, CancellationToken ct)
    {
        if (request.User is null)
            return Results.Unauthorized();

        if (string.IsNullOrWhiteSpace(request.PlanHandle))
            return Results.Problem(detail: "planHandle is required.", statusCode: StatusCodes.Status400BadRequest);

        try
        {
            var result = await service.SubscribeAsync(request.User, request.PlanHandle!, ct);
            var dto = SubscriptionDto.From(result.Subscription);
            var response = new SubscribeResponse(request.CorrelationId())
            {
                Subscription = dto,
                AlreadySubscribed = result.AlreadySubscribed,
                Message = result.AlreadySubscribed
                    ? $"You are already subscribed to {dto.PlanName ?? dto.PlanHandle} (state: {dto.State})."
                    : $"Subscribed to {dto.PlanName ?? dto.PlanHandle} (state: {dto.State}). Next billing: {FormatDate(dto.NextBillingDate)}."
            };

            // A pre-existing subscription is 200; a freshly created one is 201.
            return result.AlreadySubscribed
                ? Results.Ok(response)
                : Results.Created($"api/my-subscriptions", response);
        }
        catch (MaxioIntegrationException ex)
        {
            return SubscriptionResults.FromError(ex);
        }
    }

    private static string FormatDate(System.DateTimeOffset? date) =>
        date.HasValue ? date.Value.ToString("u") : "n/a";
}
