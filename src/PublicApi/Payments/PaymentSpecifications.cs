using Ardalis.Specification;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;
namespace Microsoft.eShopWeb.PublicApi.Payments;
public sealed class PaymentByOrderSpec : Specification<Payment> { public PaymentByOrderSpec(int id) => Query.Where(x => x.OrderId == id); }
public sealed class PaymentMethodsSpec : Specification<PaymentMethod> { public PaymentMethodsSpec(string user) => Query.Where(x => x.BuyerId == user && !x.IsDeleted); }
public sealed class AllPaymentsSpec : Specification<Payment> { }
