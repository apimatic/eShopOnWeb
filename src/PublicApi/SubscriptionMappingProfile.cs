using AutoMapper;
using Microsoft.eShopWeb.PublicApi.Maxio.Models;
using Microsoft.eShopWeb.PublicApi.SubscriptionPlanEndpoints;
using Microsoft.eShopWeb.PublicApi.SubscriptionsEndpoints;

namespace Microsoft.eShopWeb.PublicApi;

/// <summary>
/// AutoMapper profile that maps Maxio API contract models to the PublicApi response DTOs for
/// the subscription capability.
/// </summary>
public class SubscriptionMappingProfile : Profile
{
    public SubscriptionMappingProfile()
    {
        CreateMap<MaxioProduct, SubscriptionPlanDto>()
            .ForMember(d => d.Price, o => o.MapFrom(s => s.PriceInCents / 100m));

        CreateMap<MaxioSubscription, SubscriptionDto>()
            .ForMember(d => d.SubscriptionId, o => o.MapFrom(s => s.Id))
            .ForMember(d => d.PlanId, o => o.MapFrom(s => s.Product != null ? (long?)s.Product.Id : null))
            .ForMember(d => d.PlanHandle, o => o.MapFrom(s => s.Product != null ? s.Product.Handle : null))
            .ForMember(d => d.PlanName, o => o.MapFrom(s => s.Product != null ? s.Product.Name : null))
            .ForMember(d => d.PriceInCents, o => o.MapFrom(s => s.ProductPriceInCents));
    }
}
