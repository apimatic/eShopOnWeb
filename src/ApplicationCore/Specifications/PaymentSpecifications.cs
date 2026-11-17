using Ardalis.Specification;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Specifications;

public class OrderPaymentByOrderIdSpec : Specification<OrderPayment>
{
    public OrderPaymentByOrderIdSpec(int orderId)
    {
        Query.Where(p => p.OrderId == orderId);
    }
}

public class OrderPaymentsByBuyerSpec : Specification<OrderPayment>
{
    public OrderPaymentsByBuyerSpec(string buyerId)
    {
        Query.Where(p => p.BuyerId == buyerId);
    }
}

public class PaymentRefundByPaymentIdSpec : Specification<PaymentRefund>
{
    public PaymentRefundByPaymentIdSpec(int orderPaymentId)
    {
        Query.Where(r => r.OrderPaymentId == orderPaymentId)
            .OrderBy(r => r.CreatedAt);
    }
}

public class PaymentRefundByIdempotencyKeySpec : Specification<PaymentRefund>
{
    public PaymentRefundByIdempotencyKeySpec(int orderPaymentId, string idempotencyKey)
    {
        Query.Where(r => r.OrderPaymentId == orderPaymentId && r.IdempotencyKey == idempotencyKey);
    }
}

public class AllOrderPaymentsSpec : Specification<OrderPayment>
{
    public AllOrderPaymentsSpec()
    {
    }
}

public class AllPaymentRefundsSpec : Specification<PaymentRefund>
{
    public AllPaymentRefundsSpec()
    {
    }
}

public class SavedPaymentMethodByIdAndBuyerSpec : Specification<SavedPaymentMethod>
{
    public SavedPaymentMethodByIdAndBuyerSpec(int paymentMethodId, string buyerId)
    {
        Query.Where(s => s.Id == paymentMethodId && s.BuyerId == buyerId);
    }
}

public class SavedPaymentMethodsByBuyerSpec : Specification<SavedPaymentMethod>
{
    public SavedPaymentMethodsByBuyerSpec(string buyerId)
    {
        Query.Where(s => s.BuyerId == buyerId && !s.IsDeleted)
            .OrderBy(s => s.CreatedAt);
    }
}

public class FinixCustomerByBuyerSpec : Specification<FinixCustomer>
{
    public FinixCustomerByBuyerSpec(string buyerId)
    {
        Query.Where(c => c.BuyerId == buyerId);
    }
}
