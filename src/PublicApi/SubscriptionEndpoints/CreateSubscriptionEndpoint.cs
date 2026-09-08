using System.Threading;
using System.Threading.Tasks;
using Ardalis.ApiEndpoints;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.SubscriptionBilling;
using Microsoft.eShopWeb.Infrastructure.Identity;
using Swashbuckle.AspNetCore.Annotations;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// Subscribes the authenticated shopper to a plan. Idempotent: a double-click never
/// creates two customers or two subscriptions for the same shopper and plan.
/// </summary>
public sealed class CreateSubscriptionEndpoint : EndpointBaseAsync
    .WithRequest<CreateSubscriptionRequest>
    .WithActionResult<CreateSubscriptionResponse>
{
    private readonly ISubscriptionBillingService _billingService;
    private readonly UserManager<ApplicationUser> _userManager;

    public CreateSubscriptionEndpoint(ISubscriptionBillingService billingService, UserManager<ApplicationUser> userManager)
    {
        _billingService = billingService;
        _userManager = userManager;
    }

    [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
    [HttpPost("api/subscriptions")]
    [SwaggerOperation(
        Summary = "Subscribes the current shopper to a plan",
        Description = "Creates a subscription for the authenticated shopper. Returns the existing subscription (HTTP 200) instead of creating a duplicate when the shopper already has a live subscription to the same plan.",
        OperationId = "subscriptions.create",
        Tags = new[] { "SubscriptionEndpoints" })
    ]
    public override async Task<ActionResult<CreateSubscriptionResponse>> HandleAsync(CreateSubscriptionRequest request,
        CancellationToken cancellationToken = default)
    {
        var planHandle = request.PlanHandle?.Trim();
        if (string.IsNullOrWhiteSpace(planHandle))
        {
            return ErrorResult.Create(StatusCodes.Status400BadRequest, "planHandle is required.");
        }

        var userName = User.Identity?.Name;
        if (string.IsNullOrWhiteSpace(userName))
        {
            return ErrorResult.Create(StatusCodes.Status401Unauthorized, "A valid bearer token is required.");
        }

        var user = await _userManager.FindByNameAsync(userName);
        if (user is null)
        {
            return ErrorResult.Create(StatusCodes.Status404NotFound, $"The authenticated user '{userName}' could not be found.");
        }

        var email = user.Email ?? userName;
        var (firstName, lastName) = ResolveNames(request.FirstName, request.LastName, email);

        var signup = new SubscriptionSignup
        {
            CustomerReference = user.Id,
            Email = email,
            FirstName = firstName,
            LastName = lastName,
            PlanHandle = planHandle
        };

        var enrollment = await _billingService.SubscribeAsync(signup, cancellationToken);

        var response = new CreateSubscriptionResponse(request.CorrelationId())
        {
            IsNew = enrollment.IsNew,
            Subscription = SubscriptionDto.From(enrollment.Subscription)
        };

        return enrollment.IsNew
            ? StatusCode(StatusCodes.Status201Created, response)
            : response;
    }

    private static (string FirstName, string LastName) ResolveNames(string? firstName, string? lastName, string email)
    {
        var localPart = email.Split('@', 2)[0];
        if (string.IsNullOrWhiteSpace(localPart))
        {
            localPart = "Shopper";
        }

        var first = !string.IsNullOrWhiteSpace(firstName) ? firstName.Trim() : localPart;
        var last = !string.IsNullOrWhiteSpace(lastName) ? lastName.Trim() : "Customer";
        return (Truncate(first, 30), Truncate(last, 30));
    }

    private static string Truncate(string value, int maxLength)
    {
        return value.Length <= maxLength ? value : value[..maxLength];
    }
}
