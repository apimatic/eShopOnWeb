using Microsoft.eShopWeb.ApplicationCore.Entities;
using Microsoft.eShopWeb.ApplicationCore.Entities.BuyerAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.Infrastructure.PayPal;

namespace Microsoft.eShopWeb.PublicApi.PaymentShared;

// A small constructor-injected bag of the scoped services the order-payment and saved-card
// endpoints need. Built up manually from individually DI-bound route-delegate parameters
// (see each endpoint's AddRoute) rather than resolved as a whole, since MinimalApi.Endpoint's
// IEndpoint<TResult, TRequest, TDependency> only carries a single dependency type.
public class PaymentDependencies
{
    public PaymentDependencies(
        IRepository<Order> orderRepository,
        IRepository<Payment> paymentRepository,
        IRepository<Buyer> buyerRepository,
        IRepository<CatalogItem> catalogItemRepository,
        IPayPalClient payPalClient,
        PayPalOptions payPalOptions)
    {
        OrderRepository = orderRepository;
        PaymentRepository = paymentRepository;
        BuyerRepository = buyerRepository;
        CatalogItemRepository = catalogItemRepository;
        PayPalClient = payPalClient;
        PayPalOptions = payPalOptions;
    }

    public IRepository<Order> OrderRepository { get; }
    public IRepository<Payment> PaymentRepository { get; }
    public IRepository<Buyer> BuyerRepository { get; }
    public IRepository<CatalogItem> CatalogItemRepository { get; }
    public IPayPalClient PayPalClient { get; }
    public PayPalOptions PayPalOptions { get; }
}
