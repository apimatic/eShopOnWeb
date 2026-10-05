using System;
using System.Linq;
using System.Net;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Ardalis.ApiEndpoints;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Subscription;
using Microsoft.eShopWeb.Infrastructure.Identity;
using Microsoft.eShopWeb.Infrastructure.Maxio;
using Swashbuckle.AspNetCore.Annotations;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// Subscribes the authenticated user to a plan. Idempotent: repeating the same
/// subscribe (e.g. a double-click) returns the existing subscription instead of
/// creating a duplicate.
/// </summary>
[Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
public class SubscriptionCreateEndpoint : EndpointBaseAsync
    .WithRequest<CreateSubscriptionRequest>
    .WithActionResult<CreateSubscriptionResponse>
{
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly ISubscriptionBillingService _billingService;

    public SubscriptionCreateEndpoint(UserManager<ApplicationUser> userManager,
        ISubscriptionBillingService billingService)
    {
        _userManager = userManager;
        _billingService = billingService;
    }

    [HttpPost("api/subscriptions")]
    [SwaggerOperation(
        Summary = "Subscribes the authenticated user to a plan",
        Description = "Ensures a Maxio customer exists for the user and enrolls them in the plan. " +
                      "Idempotent - repeated calls return the existing subscription.",
        OperationId = "subscriptions.create",
        Tags = new[] { "SubscriptionEndpoints" })
    ]
    public override async Task<ActionResult<CreateSubscriptionResponse>> HandleAsync(CreateSubscriptionRequest request,
        CancellationToken cancellationToken = default)
    {
        var appUser = await ResolveUserAsync();
        if (appUser is null)
        {
            return Unauthorized();
        }

        if (string.IsNullOrWhiteSpace(request.ProductHandle))
        {
            return BadRequest(new { message = "productHandle is required." });
        }

        var (firstName, lastName) = DeriveNames(appUser);

        try
        {
            var details = await _billingService.SubscribeAsync(
                new SubscriptionSignupRequest(appUser.Id, appUser.Email ?? appUser.UserName ?? string.Empty, firstName, lastName, request.ProductHandle),
                cancellationToken);

            return new CreateSubscriptionResponse(request.CorrelationId())
            {
                Subscription = ToDto(details),
                WasExisting = details.WasExisting
            };
        }
        catch (MaxioApiException ex)
        {
            return MapMaxioError(ex);
        }
    }

    private async Task<ApplicationUser?> ResolveUserAsync()
    {
        var username = User.FindFirstValue(ClaimTypes.Name) ?? User.FindFirstValue("unique_name") ?? User.Identity?.Name;
        if (string.IsNullOrWhiteSpace(username))
        {
            return null;
        }

        return await _userManager.FindByNameAsync(username);
    }

    /// <summary>
    /// eShopOnWeb user records have no first/last name; derive display names from
    /// the username/email so Maxio customer records look sensible.
    /// </summary>
    private static (string FirstName, string LastName) DeriveNames(ApplicationUser appUser)
    {
        var source = appUser.UserName ?? appUser.Email ?? "eShop Shopper";
        var parts = source.Split(new[] { '@', '.', '_', '-', '+' }, StringSplitOptions.RemoveEmptyEntries);
        var first = Capitalize(parts.Length > 0 ? parts[0] : "eShop");
        var last = Capitalize(parts.Length > 1 ? parts[1] : "Shopper");
        return (first, last);
    }

    private static string Capitalize(string value) =>
        string.IsNullOrEmpty(value) ? value : char.ToUpperInvariant(value[0]) + value[1..].ToLowerInvariant();

    private static SubscriptionDto ToDto(SubscriptionDetails details) => new()
    {
        Id = details.SubscriptionId,
        State = details.State,
        PlanHandle = details.ProductHandle,
        PlanName = details.ProductName,
        Price = details.Price,
        Currency = details.Currency,
        NextBillingDate = details.NextBillingDate
    };

    internal static ActionResult MapMaxioError(MaxioApiException ex)
    {
        var errors = ex.Errors.Count > 0 ? string.Join("; ", ex.Errors) : ex.Message;
        var detail = $"{ex.Message} [{errors}]";
        var statusCode = ex.StatusCode switch
        {
            400 => StatusCodes.Status400BadRequest,
            404 => StatusCodes.Status404NotFound,
            409 => StatusCodes.Status409Conflict,
            422 => StatusCodes.Status422UnprocessableEntity,
            _ => StatusCodes.Status502BadGateway
        };

        return new ObjectResult(new ProblemDetails
        {
            Title = "Subscription billing error",
            Detail = detail,
            Status = statusCode
        })
        {
            StatusCode = statusCode
        };
    }
}