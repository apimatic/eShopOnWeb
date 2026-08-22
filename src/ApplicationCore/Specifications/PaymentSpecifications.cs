using Ardalis.Specification;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentMethodAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Specifications;

public class SavedPaymentMethodsByBuyerSpec : Specification<SavedPaymentMethod>
{
    public SavedPaymentMethodsByBuyerSpec(string buyerId)
    {
        Query.Where(p => p.BuyerId == buyerId)
            .OrderByDescending(p => p.CreatedAt);
    }
}

public class SavedPaymentMethodByIdAndBuyerSpec : Specification<SavedPaymentMethod>, ISingleResultSpecification<SavedPaymentMethod>
{
    public SavedPaymentMethodByIdAndBuyerSpec(int paymentMethodId, string buyerId)
    {
        Query.Where(p => p.Id == paymentMethodId && p.BuyerId == buyerId);
    }
}

public class OrdersInDateRangeSpec : Specification<Order>
{
    public OrdersInDateRangeSpec(System.DateTimeOffset from, System.DateTimeOffset to)
    {
        Query.Where(o =>
                (o.OrderDate >= from && o.OrderDate <= to) ||
                (o.AuthorizedAt != null && o.AuthorizedAt >= from && o.AuthorizedAt <= to) ||
                (o.CapturedAt != null && o.CapturedAt >= from && o.CapturedAt <= to))
            .Include(o => o.Refunds)
            .Include(o => o.OrderItems);
    }
}
