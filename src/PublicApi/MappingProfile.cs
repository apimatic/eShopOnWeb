using AutoMapper;
using Microsoft.eShopWeb.ApplicationCore.Entities;
using Microsoft.eShopWeb.PublicApi.CatalogBrandEndpoints;
using Microsoft.eShopWeb.PublicApi.CatalogItemEndpoints;
using Microsoft.eShopWeb.PublicApi.CatalogTypeEndpoints;
using Microsoft.eShopWeb.PublicApi.Maxio;
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

        CreateMap<MaxioProduct, SubscriptionPlanDto>()
            .ForMember(dto => dto.Price, options => options.MapFrom(src => src.PriceInCents / 100m))
            .ForMember(dto => dto.ProductFamilyName,
                options => options.MapFrom(src => src.ProductFamily != null ? src.ProductFamily.Name : null))
            .ForMember(dto => dto.ProductFamilyHandle,
                options => options.MapFrom(src => src.ProductFamily != null ? src.ProductFamily.Handle : null));

        CreateMap<MaxioSubscription, SubscriptionDto>()
            .ForMember(dto => dto.SubscriptionId, options => options.MapFrom(src => src.Id))
            .ForMember(dto => dto.Price, options => options.MapFrom(src => src.ProductPriceInCents / 100m))
            .ForMember(dto => dto.ProductPriceInCents, options => options.MapFrom(src => src.ProductPriceInCents))
            .ForMember(dto => dto.ProductHandle,
                options => options.MapFrom(src => src.Product != null ? src.Product.Handle : null))
            .ForMember(dto => dto.ProductName,
                options => options.MapFrom(src => src.Product != null ? src.Product.Name : null))
            .ForMember(dto => dto.NextBillingDate, options => options.MapFrom(src => src.CurrentPeriodEndsAt))
            .ForMember(dto => dto.CustomerId,
                options => options.MapFrom(src => src.Customer != null ? src.Customer.Id : 0))
            .ForMember(dto => dto.CustomerReference,
                options => options.MapFrom(src => src.Customer != null ? src.Customer.Reference : null));
    }
}
