using System;
using System.Collections.Generic;
using System.Linq;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class ListSubscriptionPlansResponse : BaseResponse
{
    public ListSubscriptionPlansResponse(Guid correlationId) : base(correlationId)
    {
    }

    public ListSubscriptionPlansResponse()
    {
    }

    public List<SubscriptionPlanDto> Plans { get; set; } = new();
}

public class ListMySubscriptionsResponse : BaseResponse
{
    public ListMySubscriptionsResponse(Guid correlationId) : base(correlationId)
    {
    }

    public ListMySubscriptionsResponse()
    {
    }

    public List<SubscriptionDto> Subscriptions { get; set; } = new();
}

public class CreateSubscriptionRequest : BaseRequest
{
    public string ProductHandle { get; set; } = string.Empty;
}

public class CreateSubscriptionResponse : BaseResponse
{
    public CreateSubscriptionResponse(Guid correlationId) : base(correlationId)
    {
    }

    public CreateSubscriptionResponse()
    {
    }

    public SubscriptionDto Subscription { get; set; } = new();
}

internal static class SubscriptionMapping
{
    public static SubscriptionPlanDto ToDto(ApplicationCore.Entities.SubscriptionAggregate.SubscriptionPlan plan) =>
        new()
        {
            Handle = plan.Handle,
            Name = plan.Name,
            Description = plan.Description,
            Price = plan.Price,
            Interval = plan.Interval,
            IntervalUnit = plan.IntervalUnit
        };

    public static SubscriptionDto ToDto(ApplicationCore.Entities.SubscriptionAggregate.CustomerSubscription subscription) =>
        new()
        {
            Id = subscription.Id,
            ProductHandle = subscription.ProductHandle,
            ProductName = subscription.ProductName,
            Price = subscription.Price,
            State = subscription.State,
            NextBillingDate = subscription.NextBillingDate
        };

    public static List<SubscriptionPlanDto> ToPlanDtos(
        this IEnumerable<ApplicationCore.Entities.SubscriptionAggregate.SubscriptionPlan> plans) =>
        plans.Select(ToDto).ToList();

    public static List<SubscriptionDto> ToSubscriptionDtos(
        this IEnumerable<ApplicationCore.Entities.SubscriptionAggregate.CustomerSubscription> subscriptions) =>
        subscriptions.Select(ToDto).ToList();
}
