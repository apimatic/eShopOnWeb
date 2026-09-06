using Microsoft.eShopWeb.ApplicationCore.Models;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class PlanDto
{
    public int Id { get; set; }
    public string? Handle { get; set; }
    public string? Name { get; set; }
    public string? Description { get; set; }
    public decimal Price { get; set; }
    public string? Interval { get; set; }
    public int IntervalCount { get; set; }

    public static PlanDto FromModel(PlanModel model) => new()
    {
        Id = model.Id,
        Handle = model.Handle,
        Name = model.Name,
        Description = model.Description,
        Price = model.Price,
        Interval = model.Interval,
        IntervalCount = model.IntervalCount
    };
}
