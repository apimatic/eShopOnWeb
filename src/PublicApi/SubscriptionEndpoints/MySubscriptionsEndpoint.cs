using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using Ardalis.ApiEndpoints;
using MaxioAdvancedBilling;
using MaxioAdvancedBilling.Core.ErrorResponse;
using MaxioAdvancedBilling.Core.Exceptions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Swashbuckle.AspNetCore.Annotations;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

[Authorize]
public class MySubscriptionsEndpoint : EndpointBaseAsync
    .WithoutRequest
    .WithActionResult<List<SubscriptionDto>>
{
    private readonly MaxioAdvancedBillingClient _maxioClient;
    private readonly IHttpContextAccessor _httpContextAccessor;

    public MySubscriptionsEndpoint(MaxioAdvancedBillingClient maxioClient, IHttpContextAccessor httpContextAccessor)
    {
        _maxioClient = maxioClient;
        _httpContextAccessor = httpContextAccessor;
    }

    [HttpGet("api/my-subscriptions")]
    [SwaggerOperation(
        Summary = "Get user's subscriptions",
        Description = "Retrieves the authenticated user's active subscriptions",
        OperationId = "subscriptions.getMine",
        Tags = new[] { "Subscriptions" })]
    public override async Task<ActionResult<List<SubscriptionDto>>> HandleAsync(
        CancellationToken cancellationToken = default)
    {
        try
        {
            // Extract user ID from JWT token
            var principal = _httpContextAccessor.HttpContext?.User;
            if (principal == null)
            {
                return Unauthorized("User not authenticated");
            }

            var userIdClaim = principal.FindFirst("sub") ?? principal.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier);
            if (userIdClaim == null)
            {
                return Unauthorized("User ID claim not found in token");
            }

            string userId = userIdClaim.Value;

            // Look up customer by reference (userId)
            MaxioAdvancedBilling.Models.Customer customer = null;
            try
            {
                var customerResponse = await _maxioClient.Customers.ReadCustomerByReference(
                    reference: userId,
                    ct: cancellationToken);

                customer = customerResponse?.Customer;
            }
            catch (SdkException<RawError> ex)
            {
                if (ex.Error.StatusCode == HttpStatusCode.NotFound)
                {
                    // Customer doesn't exist, return empty list
                    return Ok(new List<SubscriptionDto>());
                }

                throw;
            }

            if (customer?.Id == null)
            {
                return Ok(new List<SubscriptionDto>());
            }

            // Get customer's subscriptions
            var subscriptionsResponse = await _maxioClient.Customers.ListCustomerSubscriptions(
                customerId: (int)customer.Id!.Value,
                ct: cancellationToken);

            var subscriptions = subscriptionsResponse?
                .Select(sr => sr?.Subscription)
                .Where(s => s != null)
                .Select(s => new SubscriptionDto(
                    Id: (int)(s!.Id ?? 0),
                    ProductHandle: s.Reference ?? "unknown",
                    State: s.State?.Value ?? "unknown",
                    ActivatedAt: s.ActivatedAt,
                    CurrentPeriodEndsAt: s.CurrentPeriodEndsAt,
                    NextAssessmentAt: s.NextAssessmentAt,
                    ProductPricePerMonth: (s.ProductPriceInCents ?? 0) / 100m,
                    Reference: s.Reference
                ))
                .ToList() ?? new List<SubscriptionDto>();

            return Ok(subscriptions);
        }
        catch (SdkException<RawError> ex)
        {
            var statusCode = (int)(ex.Error.StatusCode);
            return StatusCode(statusCode,
                $"Maxio error: {ex.Error.ReadAsString()}");
        }
        catch (Exception ex)
        {
            return BadRequest($"Error retrieving subscriptions: {ex.Message}");
        }
    }
}
