using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.eShopWeb.ApplicationCore.Entities;
using Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

/// <summary>The scoped services the order endpoints need, aggregated for injection.</summary>
public sealed class OrderEndpointServices
{
    public OrderEndpointServices(
        IHttpContextAccessor httpContextAccessor,
        IRepository<Order> orders,
        IRepository<CatalogItem> catalogItems,
        IReadRepository<Notification> notifications,
        IUriComposer uriComposer,
        IOrderNotificationService notifier)
    {
        HttpContextAccessor = httpContextAccessor;
        Orders = orders;
        CatalogItems = catalogItems;
        Notifications = notifications;
        UriComposer = uriComposer;
        Notifier = notifier;
    }

    public IHttpContextAccessor HttpContextAccessor { get; }
    public IRepository<Order> Orders { get; }
    public IRepository<CatalogItem> CatalogItems { get; }
    public IReadRepository<Notification> Notifications { get; }
    public IUriComposer UriComposer { get; }
    public IOrderNotificationService Notifier { get; }

    public ClaimsPrincipal? User => HttpContextAccessor.HttpContext?.User;
}
