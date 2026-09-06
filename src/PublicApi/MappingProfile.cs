using AutoMapper;
using Microsoft.eShopWeb.ApplicationCore.Entities;
using Microsoft.eShopWeb.ApplicationCore.Models.Subscriptions;
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

        CreateMap<SubscriptionPlan, SubscriptionPlanDto>()
            .ForMember(dto => dto.DisplayPrice, options => options.MapFrom(src =>
                SubscriptionPriceFormatter.Recurring(src.PriceInCents, src.Currency, src.Interval, src.IntervalUnit)));
        CreateMap<CustomerSubscription, SubscriptionDto>()
            .ForMember(dto => dto.DisplayPrice, options => options.MapFrom(src =>
                SubscriptionPriceFormatter.Recurring(src.PriceInCents, src.Currency, src.Interval, src.IntervalUnit)));
    }
}
