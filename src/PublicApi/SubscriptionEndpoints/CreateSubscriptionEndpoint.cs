using System.Security.Claims;
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
/// Subscribes the calling user to a Maxio plan. Ensures a Maxio customer exists for the
/// caller (idempotent - a double-click never creates two customers or two subscriptions to
/// the same plan) and enrolls them in the requested plan.
/// </summary>
public class CreateSubscriptionEndpoint : IEndpoint<IResult, CreateSubscriptionRequest, IMaxioClient>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/subscriptions",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (CreateSubscriptionRequest request, ClaimsPrincipal user, IMaxioClient maxioClient) =>
            {
                request.UserEmail = user.Identity?.Name ?? string.Empty;
                return await HandleAsync(request, maxioClient);
            })
            .Produces<CreateSubscriptionResponse>()
            .Produces<CreateSubscriptionResponse>(StatusCodes.Status200OK)
            .WithTags("SubscriptionEndpoints");
    }

    public async Task<IResult> HandleAsync(CreateSubscriptionRequest request, IMaxioClient maxioClient)
    {
        var response = new CreateSubscriptionResponse(request.CorrelationId());

        if (string.IsNullOrWhiteSpace(request.UserEmail))
        {
            return Results.Unauthorized();
        }

        if (string.IsNullOrWhiteSpace(request.PlanHandle))
        {
            return Results.BadRequest("PlanHandle is required.");
        }

        var (firstName, lastName) = ResolveBillingName(request);

        var result = await maxioClient.SubscribeAsync(
            customerReference: request.UserEmail,
            email: request.UserEmail,
            firstName: firstName,
            lastName: lastName,
            planHandle: request.PlanHandle);

        response.WasNewlyCreated = result.WasNewlyCreated;
        response.Subscription = new MySubscriptionDto
        {
            SubscriptionId = result.Subscription.Id,
            PlanHandle = result.Subscription.PlanHandle,
            PlanName = result.Subscription.PlanName,
            Price = result.Subscription.PriceInCents / 100m,
            State = result.Subscription.State,
            CurrentPeriodEndsAt = result.Subscription.CurrentPeriodEndsAt,
            NextBillingDate = result.Subscription.NextAssessmentAt,
            CreatedAt = result.Subscription.CreatedAt,
        };

        return result.WasNewlyCreated
            ? Results.Created($"api/my-subscriptions", response)
            : Results.Ok(response);
    }

    private static (string FirstName, string LastName) ResolveBillingName(CreateSubscriptionRequest request)
    {
        if (!string.IsNullOrWhiteSpace(request.FirstName) && !string.IsNullOrWhiteSpace(request.LastName))
        {
            return (request.FirstName!, request.LastName!);
        }

        var localPart = request.UserEmail.Split('@')[0];
        return (request.FirstName ?? localPart, request.LastName ?? "eShopOnWeb Customer");
    }
}
