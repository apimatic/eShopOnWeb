using System;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Maxio;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// Subscribes the calling user to a plan. Ensures a Maxio customer exists for the caller
/// (idempotent - a double-click never creates two customers or two active subscriptions).
/// </summary>
public class CreateSubscriptionEndpoint : IEndpoint<IResult, CreateSubscriptionRequest, IMaxioBillingService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/subscriptions",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (CreateSubscriptionRequest request, HttpContext httpContext, IMaxioBillingService billingService) =>
            {
                var callerEmail = httpContext.User.Identity?.Name ?? string.Empty;
                request.CallerReference = callerEmail;
                request.CallerEmail = callerEmail;

                return await HandleAsync(request, billingService);
            })
            .Produces<CreateSubscriptionResponse>()
            .WithTags("SubscriptionEndpoints");
    }

    public async Task<IResult> HandleAsync(CreateSubscriptionRequest request, IMaxioBillingService billingService)
    {
        var response = new CreateSubscriptionResponse(request.CorrelationId());

        var (firstName, lastName) = ResolveName(request);

        var subscription = await billingService.SubscribeAsync(new SubscribeRequest
        {
            UserReference = request.CallerReference,
            Email = request.CallerEmail,
            FirstName = firstName,
            LastName = lastName,
            PlanHandle = request.PlanHandle
        });

        response.Subscription = subscription.ToDto();

        return Results.Ok(response);
    }

    // ApplicationUser (ASP.NET Core Identity) has no first/last name fields in this app, so we
    // fall back to something derived from the email local-part when the caller doesn't supply one.
    private static (string FirstName, string LastName) ResolveName(CreateSubscriptionRequest request)
    {
        if (!string.IsNullOrWhiteSpace(request.FirstName) && !string.IsNullOrWhiteSpace(request.LastName))
        {
            return (request.FirstName, request.LastName);
        }

        var localPart = request.CallerEmail.Split('@').FirstOrDefault() ?? "Member";
        var derivedFirstName = localPart.Length > 0
            ? char.ToUpperInvariant(localPart[0]) + localPart[1..]
            : "Member";

        return (
            string.IsNullOrWhiteSpace(request.FirstName) ? derivedFirstName : request.FirstName,
            string.IsNullOrWhiteSpace(request.LastName) ? "Customer" : request.LastName);
    }
}
