using System;
using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class CreateSubscriptionEndpoint : IEndpoint<IResult, CreateSubscriptionRequestWithUser, IMaxioSubscriptionService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/subscriptions",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
            async (ClaimsPrincipal user, CreateSubscriptionRequest request, IMaxioSubscriptionService service) =>
            {
                return await HandleAsync(new CreateSubscriptionRequestWithUser(request, user), service);
            })
            .Produces<CreateSubscriptionResponse>(StatusCodes.Status201Created)
            .WithTags("SubscriptionEndpoints");
    }

    public async Task<IResult> HandleAsync(CreateSubscriptionRequestWithUser request, IMaxioSubscriptionService service)
    {
        var response = new CreateSubscriptionResponse(request.BaseRequest.CorrelationId());

        try
        {
            var userId = request.User.FindFirst(ClaimTypes.NameIdentifier)?.Value
                ?? throw new InvalidOperationException("User ID not found in token");

            var userEmail = request.User.FindFirst(ClaimTypes.Email)?.Value
                ?? throw new InvalidOperationException("User email not found in token");

            var firstName = request.User.FindFirst(ClaimTypes.GivenName)?.Value ?? "Unknown";
            var lastName = request.User.FindFirst(ClaimTypes.Surname)?.Value ?? "User";

            var subscription = await service.CreateSubscriptionAsync(
                userId,
                userEmail,
                firstName,
                lastName,
                request.BaseRequest.ProductHandle);

            response.Subscription = new SubscriptionResponse
            {
                Id = subscription.Id,
                Reference = subscription.Reference,
                State = subscription.State,
                ProductPriceInCents = subscription.ProductPriceInCents,
                CurrentPeriodEndsAt = subscription.CurrentPeriodEndsAt,
                NextAssessmentAt = subscription.NextAssessmentAt
            };

            return Results.Created($"api/subscriptions/{subscription.Id}", response);
        }
        catch (ArgumentException ex)
        {
            response.ErrorMessage = $"Invalid request: {ex.Message}";
            return Results.BadRequest(response);
        }
        catch (Exception ex)
        {
            response.ErrorMessage = $"Failed to create subscription: {ex.Message}";
            return Results.BadRequest(response);
        }
    }
}

public class CreateSubscriptionRequest : BaseRequest
{
    public string ProductHandle { get; set; } = string.Empty;
}

public class CreateSubscriptionRequestWithUser
{
    public CreateSubscriptionRequestWithUser(CreateSubscriptionRequest request, ClaimsPrincipal user)
    {
        BaseRequest = request;
        User = user;
    }
    public CreateSubscriptionRequest BaseRequest { get; }
    public ClaimsPrincipal User { get; }
}

public class CreateSubscriptionResponse : BaseResponse
{
    public CreateSubscriptionResponse() : base() { }
    public CreateSubscriptionResponse(Guid correlationId) : base(correlationId) { }

    public SubscriptionResponse? Subscription { get; set; }
    public string? ErrorMessage { get; set; }
}

public class SubscriptionResponse
{
    public int Id { get; set; }
    public string? Reference { get; set; }
    public string State { get; set; } = string.Empty;
    public long? ProductPriceInCents { get; set; }
    public DateTimeOffset? CurrentPeriodEndsAt { get; set; }
    public DateTimeOffset? NextAssessmentAt { get; set; }
}
