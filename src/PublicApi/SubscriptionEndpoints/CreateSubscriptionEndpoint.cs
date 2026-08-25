using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Net;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.Infrastructure.Identity;
using Microsoft.eShopWeb.PublicApi.Maxio;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// Subscribes the authenticated user to a plan (Maxio product) in the configured product family.
/// Ensures a Maxio customer exists for the user and enrolls them idempotently: repeating the
/// same subscribe call returns the existing subscription instead of creating a duplicate.
/// </summary>
public class CreateSubscriptionEndpoint : IEndpoint<IResult, CreateSubscriptionRequest, MaxioClient, UserManager<ApplicationUser>>
{
    // States in which a subscription no longer bills; a matching subscription in any other
    // state is treated as an active enrollment and returned instead of creating a duplicate.
    private static readonly HashSet<string> EndOfLifeStates = new(StringComparer.OrdinalIgnoreCase)
    {
        "canceled", "expired", "failed_to_create", "trial_ended"
    };

    private readonly IHttpContextAccessor _httpContextAccessor;

    public CreateSubscriptionEndpoint(IHttpContextAccessor httpContextAccessor)
    {
        _httpContextAccessor = httpContextAccessor;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/subscriptions",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (CreateSubscriptionRequest request, MaxioClient maxioClient, UserManager<ApplicationUser> userManager) =>
            {
                return await HandleAsync(request, maxioClient, userManager);
            })
            .Produces<CreateSubscriptionResponse>()
            .WithTags("SubscriptionEndpoints");
    }

    public async Task<IResult> HandleAsync(CreateSubscriptionRequest request, MaxioClient maxioClient, UserManager<ApplicationUser> userManager)
    {
        var response = new CreateSubscriptionResponse(request.CorrelationId());

        var user = await ResolveUserAsync(userManager, _httpContextAccessor.HttpContext);
        if (user is null)
        {
            return Results.Unauthorized();
        }

        try
        {
            var plans = await maxioClient.ListPlansAsync();
            var plan = plans.FirstOrDefault(p => string.Equals(p.Handle, request.ProductHandle, StringComparison.OrdinalIgnoreCase));
            if (plan is null)
            {
                return Results.NotFound(new { message = $"No subscription plan with handle '{request.ProductHandle}' is available." });
            }

            var customer = await EnsureCustomerAsync(maxioClient, user);

            // Deterministic reference makes a repeated subscribe (double-click, retry) idempotent.
            var subscriptionReference = $"eshop-{user.Id}-{plan.Handle}";
            var existing = await maxioClient.FindSubscriptionByReferenceAsync(subscriptionReference);
            if (existing is not null && !EndOfLifeStates.Contains(existing.State))
            {
                response.Subscription = SubscriptionMapper.ToDto(existing);
                response.IdempotentReplay = true;
                return Results.Ok(response);
            }

            if (existing is not null)
            {
                // A previous subscription with this reference reached end of life; re-subscribe
                // under a fresh unique reference.
                subscriptionReference = $"{subscriptionReference}-{DateTimeOffset.UtcNow.ToUnixTimeSeconds()}";
            }

            var subscription = await maxioClient.CreateSubscriptionAsync(plan.Handle!, customer.Reference!, subscriptionReference);
            response.Subscription = SubscriptionMapper.ToDto(subscription);
            return Results.Created($"api/my-subscriptions", response);
        }
        catch (MaxioConfigurationException ex)
        {
            return Results.Problem(ex.Message, statusCode: (int)HttpStatusCode.ServiceUnavailable);
        }
        catch (MaxioApiException ex)
        {
            var status = ex.StatusCode == HttpStatusCode.UnprocessableEntity
                ? (int)HttpStatusCode.UnprocessableEntity
                : (int)HttpStatusCode.BadGateway;
            return Results.Problem($"Maxio billing error: {ex.ResponseBody}", statusCode: status);
        }
    }

    internal static async Task<ApplicationUser?> ResolveUserAsync(UserManager<ApplicationUser> userManager, HttpContext? httpContext)
    {
        var username = httpContext?.User.Identity?.Name;
        if (string.IsNullOrEmpty(username))
        {
            return null;
        }
        return await userManager.FindByNameAsync(username);
    }

    internal static async Task<MaxioCustomer> EnsureCustomerAsync(MaxioClient maxioClient, ApplicationUser user)
    {
        var customer = await maxioClient.FindCustomerByReferenceAsync(user.Id);
        if (customer is not null)
        {
            return customer;
        }

        var email = user.Email ?? user.UserName ?? $"{user.Id}@unknown.local";
        var (firstName, lastName) = DeriveNames(email);
        return await maxioClient.CreateCustomerAsync(firstName, lastName, email, user.Id);
    }

    private static (string FirstName, string LastName) DeriveNames(string email)
    {
        var localPart = email.Split('@')[0];
        var parts = localPart.Split(new[] { '.', '_', '-' }, StringSplitOptions.RemoveEmptyEntries);
        return parts.Length >= 2
            ? (Capitalize(parts[0]), Capitalize(parts[1]))
            : (Capitalize(localPart), "Customer");
    }

    private static string Capitalize(string value) =>
        string.IsNullOrEmpty(value) ? value : char.ToUpperInvariant(value[0]) + value[1..];
}

public class CreateSubscriptionRequest : BaseRequest
{
    [Required]
    public string ProductHandle { get; set; } = string.Empty;
}

public class CreateSubscriptionResponse : BaseResponse
{
    public CreateSubscriptionResponse(Guid correlationId) : base(correlationId)
    {
    }

    public SubscriptionDto? Subscription { get; set; }

    /// <summary>True when an existing subscription was returned instead of creating a new one.</summary>
    public bool IdempotentReplay { get; set; }
}
