using System;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.Infrastructure.Identity;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class CreateSubscriptionRequest : BaseRequest
{
    /// <summary>Handle of the plan to subscribe to; the configured default plan when omitted.</summary>
    public string? PlanHandle { get; set; }
    public string? FirstName { get; set; }
    public string? LastName { get; set; }
}

public class CreateSubscriptionResponse : BaseResponse
{
    public CreateSubscriptionResponse(Guid correlationId) : base(correlationId)
    {
    }

    public CreateSubscriptionResponse()
    {
    }

    public SubscriptionDto? Subscription { get; set; }
}

/// <summary>
/// Subscribes the authenticated user to a plan
/// </summary>
public class CreateSubscriptionEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/subscriptions",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (CreateSubscriptionRequest? request, ClaimsPrincipal user, ISubscriptionService subscriptions,
                UserManager<ApplicationUser> userManager, CancellationToken cancellationToken) =>
            {
                return await HandleAsync(request, user, subscriptions, userManager, cancellationToken);
            })
            .Produces<CreateSubscriptionResponse>(StatusCodes.Status201Created)
            .WithTags("SubscriptionEndpoints");
    }

    public async Task<IResult> HandleAsync(CreateSubscriptionRequest? request, ClaimsPrincipal user,
        ISubscriptionService subscriptions, UserManager<ApplicationUser> userManager, CancellationToken cancellationToken)
    {
        request ??= new CreateSubscriptionRequest();
        var response = new CreateSubscriptionResponse(request.CorrelationId());

        var userKey = SubscriptionEndpointSupport.UserKey(user);
        if (string.IsNullOrWhiteSpace(userKey)) return Results.Unauthorized();

        var identityUser = await userManager.FindByNameAsync(userKey);
        var email = identityUser?.Email ?? (userKey.Contains('@') ? userKey : null);
        if (string.IsNullOrWhiteSpace(email))
            return Results.Problem("The user has no email address on file.", statusCode: StatusCodes.Status400BadRequest);

        // Names are required by the billing system; derive them from the email when the caller gave none.
        var local = email.Contains('@') ? email.Substring(0, email.IndexOf('@')) : email;
        try
        {
            var summary = await subscriptions.SubscribeAsync(
                new SubscribeRequest(userKey, email,
                    string.IsNullOrWhiteSpace(request.FirstName) ? local : request.FirstName.Trim(),
                    string.IsNullOrWhiteSpace(request.LastName) ? local : request.LastName.Trim(),
                    request.PlanHandle),
                cancellationToken);
            response.Subscription = SubscriptionDto.From(summary);
            return Results.Created("api/my-subscriptions", response);
        }
        catch (SubscriptionBillingException ex)
        {
            return SubscriptionEndpointSupport.Problem(ex);
        }
    }
}
