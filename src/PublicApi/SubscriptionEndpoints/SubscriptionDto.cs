using System;
using Microsoft.eShopWeb.ApplicationCore.Models;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class SubscriptionDto
{
    public int Id { get; set; }
    public string? State { get; set; }
    public string? ProductHandle { get; set; }
    public string? ProductName { get; set; }
    public decimal Price { get; set; }
    public DateTimeOffset? CurrentPeriodStartedAt { get; set; }
    public DateTimeOffset? CurrentPeriodEndsAt { get; set; }
    public DateTimeOffset? NextAssessmentAt { get; set; }

    public static SubscriptionDto FromModel(SubscriptionModel model) => new()
    {
        Id = model.Id,
        State = model.State,
        ProductHandle = model.ProductHandle,
        ProductName = model.ProductName,
        Price = model.Price,
        CurrentPeriodStartedAt = model.CurrentPeriodStartedAt,
        CurrentPeriodEndsAt = model.CurrentPeriodEndsAt,
        NextAssessmentAt = model.NextAssessmentAt
    };
}
