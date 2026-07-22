using AutoMapper;
using Microsoft.eShopWeb.ApplicationCore.Entities;
using Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;
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
            .ForMember(dto => dto.Id, options => options.MapFrom(src => src.ProviderProductId));
        CreateMap<Subscription, SubscriptionDto>()
            .ForMember(dto => dto.CustomerId, options => options.MapFrom(src => src.ProviderCustomerId))
            .ForMember(dto => dto.State, options => options.MapFrom(src => src.State.ToString()));
        CreateMap<PlanChangePreview, PlanChangePreviewDto>()
            .ForMember(dto => dto.Timing, options => options.MapFrom(src => src.Timing.ToString()));
    }
}
