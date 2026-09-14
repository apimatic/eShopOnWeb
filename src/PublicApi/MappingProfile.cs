using AutoMapper;
using Microsoft.eShopWeb.ApplicationCore.Entities;
using Microsoft.eShopWeb.ApplicationCore.Subscriptions;
using Microsoft.eShopWeb.PublicApi.CatalogBrandEndpoints;
using Microsoft.eShopWeb.PublicApi.CatalogItemEndpoints;
using Microsoft.eShopWeb.PublicApi.CatalogTypeEndpoints;
using Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

namespace Microsoft.eShopWeb.PublicApi;

public class MappingProfile : Profile
{
    public MappingProfile()
    {
        CreateMap<CatalogItem, CatalogItemDto>();
        CreateMap<CatalogType, CatalogTypeDto>()
            .ForMember(dto => dto.Name, options => options.MapFrom(src => src.Type));
        CreateMap<CatalogBrand, CatalogBrandDto>()
            .ForMember(dto => dto.Name, options => options.MapFrom(src => src.Brand));

        // Money crosses the wire twice: in cents, exactly as the billing system stores it, and as a
        // decimal for display. Only the cents value is ever used for arithmetic.
        CreateMap<SubscriptionPlan, SubscriptionPlanDto>()
            .ForMember(dto => dto.Price, options => options.MapFrom(src => src.PriceInCents / 100m))
            .ForMember(dto => dto.SetupFee, options => options.MapFrom(src => src.SetupFeeInCents / 100m));

        CreateMap<CustomerSubscription, SubscriptionDto>()
            .ForMember(dto => dto.NextBillingDate, options => options.MapFrom(src => src.NextBillingAt))
            .ForMember(dto => dto.PlanPrice, options => options.MapFrom(src => src.PlanPriceInCents / 100m))
            .ForMember(dto => dto.Balance, options => options.MapFrom(src => src.BalanceInCents / 100m));
    }
}
