using AutoMapper;
using Microsoft.eShopWeb.ApplicationCore.Entities;
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
            .ForMember(dto => dto.Price, options => options.MapFrom(src => src.PriceInCents / 100m));
        CreateMap<SubscriptionInfo, SubscriptionInfoDto>()
            .ForMember(dto => dto.ProductPrice, options => options.MapFrom(src => src.ProductPriceInCents / 100m));
    }
}
