using Ardalis.Specification;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Specifications;

public sealed class OrderPaymentByOrderIdSpec : Specification<OrderPayment>
{
    public OrderPaymentByOrderIdSpec(int orderId)
    {
        Query.Where(p => p.OrderId == orderId);
    }
}

public sealed class OrderPaymentsByBuyerSpec : Specification<OrderPayment>
{
    public OrderPaymentsByBuyerSpec(string buyerId)
    {
        Query.Where(p => p.BuyerId == buyerId);
    }
}

public sealed class AllOrderPaymentsSpec : Specification<OrderPayment>
{
}

public sealed class SavedPaymentMethodsByBuyerSpec : Specification<SavedPaymentMethod>
{
    public SavedPaymentMethodsByBuyerSpec(string buyerId)
    {
        Query.Where(p => p.BuyerId == buyerId && !p.IsDeleted)
            .OrderByDescending(p => p.CreatedAt);
    }
}

public sealed class SavedPaymentMethodByIdAndBuyerSpec : Specification<SavedPaymentMethod>
{
    public SavedPaymentMethodByIdAndBuyerSpec(int paymentMethodId, string buyerId)
    {
        Query.Where(p => p.Id == paymentMethodId && p.BuyerId == buyerId && !p.IsDeleted);
    }
}

public sealed class ShopperFinixProfileByBuyerSpec : Specification<ShopperFinixProfile>
{
    public ShopperFinixProfileByBuyerSpec(string buyerId)
    {
        Query.Where(p => p.BuyerId == buyerId);
    }
}

public sealed class PaymentRefundsByOrderPaymentSpec : Specification<PaymentRefund>
{
    public PaymentRefundsByOrderPaymentSpec(int orderPaymentId)
    {
        Query.Where(r => r.OrderPaymentId == orderPaymentId)
            .OrderBy(r => r.CreatedAt);
    }
}

public sealed class PaymentRefundByKeySpec : Specification<PaymentRefund>
{
    public PaymentRefundByKeySpec(int orderPaymentId, string idempotencyKey)
    {
        Query.Where(r => r.OrderPaymentId == orderPaymentId && r.IdempotencyKey == idempotencyKey);
    }
}

public sealed class AllPaymentRefundsSpec : Specification<PaymentRefund>
{
}

public sealed class PaymentRefundsByBuyerSpec : Specification<PaymentRefund>
{
    public PaymentRefundsByBuyerSpec(string buyerId)
    {
        Query.Where(r => r.BuyerId == buyerId);
    }
}
