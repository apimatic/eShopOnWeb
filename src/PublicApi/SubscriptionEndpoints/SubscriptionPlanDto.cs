using System.Threading.Tasks;
namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;  public class SubscriptionPlanDto {     public int Id { get; set; }     public string Handle { get; set; } = string.Empty;     public string Name { get; set; } = string.Empty;     public decimal PriceMonthly { get; set; } }
