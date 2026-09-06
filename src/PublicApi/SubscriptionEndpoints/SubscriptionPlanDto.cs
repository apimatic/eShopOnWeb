using System;
using Swashbuckle.AspNetCore.Annotations;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class SubscriptionPlanDto
{
    public int Id { get; set; }
    public string Handle { get; set; } = "";
    public string Name { get; set; } = "";
    public string? Description { get; set; }
    public decimal? Price { get; set; }
}

public class ListSubscriptionPlansResponse : BaseResponse
{
    public SubscriptionPlanDto[] Plans { get; set; } = Array.Empty<SubscriptionPlanDto>();
}
