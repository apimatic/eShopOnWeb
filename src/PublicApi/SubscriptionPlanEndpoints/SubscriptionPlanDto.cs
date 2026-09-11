using System;
using System.Collections.Generic;
using System.Threading.Tasks;
namespace Microsoft.eShopWeb.PublicApi.SubscriptionPlanEndpoints;

public class SubscriptionPlanDto
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public string Handle { get; set; } = "";
    public decimal Price { get; set; }
    public string Interval { get; set; } = "month";
    public bool Taxable { get; set; }
    public bool RequiresCreditCard { get; set; }
}
