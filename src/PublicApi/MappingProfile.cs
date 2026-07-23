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

        CreateMap<SubscriptionPlan, SubscriptionPlanDto>();
        CreateMap<Subscription, SubscriptionDto>();
        CreateMap<PlanChangePreview, PlanChangePreviewDto>();
        CreateMap<UsageReport, UsageReportDto>()
            .ForMember(dto => dto.UsageId, options => options.MapFrom(src => src.Record.Id))
            .ForMember(dto => dto.SubscriptionId, options => options.MapFrom(src => src.Record.SubscriptionId))
            .ForMember(dto => dto.ComponentId, options => options.MapFrom(src => src.Record.ComponentId))
            .ForMember(dto => dto.ComponentHandle, options => options.MapFrom(src => src.Record.ComponentHandle))
            .ForMember(dto => dto.Quantity, options => options.MapFrom(src => src.Record.Quantity))
            .ForMember(dto => dto.Memo, options => options.MapFrom(src => src.Record.Memo))
            .ForMember(dto => dto.RecordedAt, options => options.MapFrom(src => src.Record.CreatedAt));
    }
}
