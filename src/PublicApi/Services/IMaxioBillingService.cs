using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.PublicApi.Services;

public interface IMaxioBillingService
{
    Task<IEnumerable<SubscriptionPlanDto>> GetPlansAsync();
    Task<SubscriptionResultDto?> SubscribeAsync(string userReference, string planHandle, string? email = null, string? firstName = null, string? lastName = null);
    Task<IEnumerable<MySubscriptionDto>> GetSubscriptionsForCustomerAsync(string userReference);
}

public record SubscriptionPlanDto(string Handle, string Name, decimal Price, string Interval, string IntervalUnit, int ProductId);
public record SubscriptionResultDto(bool Success, string? Message, int? SubscriptionId, string? State, DateTime? NextBillingDate, string? PlanName, decimal? PlanPrice);
public record MySubscriptionDto(int Id, string State, string PlanName, decimal Price, DateTime? NextBillingDate, DateTime? ActivatedAt);
