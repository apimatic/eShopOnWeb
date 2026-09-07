using System;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using MinimalApi.Endpoint;
using MaxioAdvancedBilling;
using MaxioAdvancedBilling.Core.ErrorResponse;
using MaxioAdvancedBilling.Core.Exceptions;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// List subscriptions for the authenticated user
/// </summary>
public partial class ListUserSubscriptionsEndpoint : IEndpoint<IResult>
{
    private readonly MaxioAdvancedBillingClient _client;
    private readonly IConfiguration _configuration;
    private readonly ILogger<ListUserSubscriptionsEndpoint> _logger;

    public ListUserSubscriptionsEndpoint(MaxioAdvancedBillingClient client, IConfiguration configuration, ILogger<ListUserSubscriptionsEndpoint> logger)
    {
        _client = client;
        _configuration = configuration;
        _logger = logger;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/my-subscriptions",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
            async (HttpContext context, MaxioAdvancedBillingClient client, IConfiguration config, ILogger<ListUserSubscriptionsEndpoint> logger) =>
            {
                return await HandleAsync(context, client, config, logger);
            })
            .Produces<ListUserSubscriptionsResponse>()
            .WithTags("SubscriptionEndpoints");
    }

    public async Task<IResult> HandleAsync()
    {
        // This interface method is not used; the implementation is in the lambda above
        return Results.StatusCode(500);
    }

    private async Task<IResult> HandleAsync(HttpContext context, MaxioAdvancedBillingClient client, IConfiguration config, ILogger<ListUserSubscriptionsEndpoint> logger)
    {
        var response = new ListUserSubscriptionsResponse(Guid.NewGuid());

        // Get the authenticated user
        var userNameClaim = context.User.FindFirst(ClaimTypes.Name);
        if (userNameClaim == null || string.IsNullOrEmpty(userNameClaim.Value))
        {
            logger.LogWarning("Subscriptions list attempted without valid user claim");
            return Results.Unauthorized();
        }

        var userName = userNameClaim.Value;

        try
        {
            // Look up customer
            MaxioAdvancedBilling.Models.CustomerResponse? customer = null;
            try
            {
                customer = await client.Customers.ReadCustomerByReference(
                    reference: userName,
                    ct: CancellationToken.None);
            }
            catch (SdkException<RawError> ex) when (ex.Error.StatusCode == System.Net.HttpStatusCode.NotFound)
            {
                // Customer doesn't exist, return empty list
                return Results.Ok(response);
            }

            if (customer?.Customer?.Id == null || customer.Customer.Id <= 0)
            {
                logger.LogWarning("Customer lookup returned no ID for reference {Reference}", userName);
                return Results.Ok(response);
            }

            // Get customer's subscriptions
            var subscriptions = await client.Customers.ListCustomerSubscriptions(
                customerId: customer.Customer.Id.Value,
                ct: CancellationToken.None);

            foreach (var subResponse in subscriptions)
            {
                var sub = subResponse?.Subscription;
                if (sub?.Id.HasValue == true)
                {
                    response.Subscriptions.Add(new SubscriptionDto
                    {
                        Id = sub.Id.Value,
                        State = sub.State?.Value ?? "",
                        ProductName = sub.Product?.Name ?? "",
                        ProductHandle = sub.Product?.Handle ?? "",
                        CurrentBillingAmountInCents = sub.CurrentBillingAmountInCents ?? 0,
                        CurrentPeriodStartsAt = sub.CurrentPeriodStartedAt,
                        CurrentPeriodEndsAt = sub.CurrentPeriodEndsAt,
                        NextAssessmentAt = sub.NextAssessmentAt,
                        ActivatedAt = sub.ActivatedAt,
                        CanceledAt = sub.CanceledAt
                    });
                }
            }

            return Results.Ok(response);
        }
        catch (SdkException<RawError> ex)
        {
            logger.LogError(ex, "Error fetching subscriptions for user {UserName}", userName);
            return Results.StatusCode((int?)ex.Error.StatusCode ?? 500);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Unexpected error fetching subscriptions");
            return Results.StatusCode(500);
        }
    }
}
