using System.Collections.Generic;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class SubscriptionPlanDto
{
    public string Handle { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public decimal Price { get; set; }
    public int PriceInCents { get; set; }
    public int Interval { get; set; }
    public string IntervalUnit { get; set; } = string.Empty;
    public bool RequireCreditCard { get; set; }
}

public class SubscriptionDto
{
    public int Id { get; set; }
    public string? ProductHandle { get; set; }
    public string? ProductName { get; set; }
    public decimal Price { get; set; }
    public int PriceInCents { get; set; }
    public string State { get; set; } = string.Empty;
    public System.DateTimeOffset? NextBillingDate { get; set; }
}

public static class SubscriptionDtoMapper
{
    public static SubscriptionPlanDto ToDto(this ApplicationCore.Entities.SubscriptionAggregate.SubscriptionPlan plan) =>
        new()
        {
            Handle = plan.Handle,
            Name = plan.Name,
            Description = plan.Description,
            Price = plan.Price,
            PriceInCents = plan.PriceInCents,
            Interval = plan.Interval,
            IntervalUnit = plan.IntervalUnit,
            RequireCreditCard = plan.RequireCreditCard
        };

    public static SubscriptionDto ToDto(this ApplicationCore.Entities.SubscriptionAggregate.SubscriptionDetails subscription) =>
        new()
        {
            Id = subscription.Id,
            ProductHandle = subscription.ProductHandle,
            ProductName = subscription.ProductName,
            Price = subscription.Price,
            PriceInCents = subscription.ProductPriceInCents,
            State = subscription.State,
            NextBillingDate = subscription.NextBillingDate
        };

    public static List<TOut> ConvertAll<TIn, TOut>(this IReadOnlyList<TIn> source, System.Func<TIn, TOut> map)
    {
        var result = new List<TOut>(source.Count);
        foreach (var item in source)
        {
            result.Add(map(item));
        }

        return result;
    }
}
