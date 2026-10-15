using System;

namespace Microsoft.eShopWeb.PublicApi.Subscriptions;

public sealed record SubscriptionPlanDto(string Handle, string Name, decimal MonthlyPrice);
public sealed record SubscribeRequest(string PlanHandle);
public sealed record SubscriptionDto(int Id, string PlanHandle, string PlanName, decimal Price, string State, DateTimeOffset? NextBillingDate);
