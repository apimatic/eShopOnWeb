using System;
using System.Linq;
using Ardalis.Specification;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentMethodAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Specifications;

public class PaymentByOrderIdSpec : Specification<OrderPayment>
{
    public PaymentByOrderIdSpec(int orderId)
    {
        Query.Where(p => p.OrderId == orderId).Include(p => p.Refunds);
    }
}

public class PaymentByIdSpec : Specification<OrderPayment>
{
    public PaymentByIdSpec(int paymentId)
    {
        Query.Where(p => p.Id == paymentId).Include(p => p.Refunds);
    }
}

public class PaymentsByOrderIdsSpec : Specification<OrderPayment>
{
    public PaymentsByOrderIdsSpec(int[] orderIds)
    {
        Query.Where(p => orderIds.Contains(p.OrderId)).Include(p => p.Refunds);
    }
}

/// <summary>Payments with a PayPal footprint (anything past order creation) — the reconciliation population.</summary>
public class PaymentsWithProcessorActivitySpec : Specification<OrderPayment>
{
    public PaymentsWithProcessorActivitySpec()
    {
        Query.Where(p => p.PayPalOrderId != null).Include(p => p.Refunds);
    }
}

/// <summary>Payments a sweeper must settle: an unknown outcome, or a transitional state nobody can still own.</summary>
public class PaymentsNeedingSettlementSpec : Specification<OrderPayment>
{
    public PaymentsNeedingSettlementSpec(DateTimeOffset staleBefore)
    {
        Query.Where(p =>
                p.OutcomeUnknownSince != null
                || ((p.Status == PaymentStatus.Authorizing || p.Status == PaymentStatus.Reauthorizing
                     || p.Status == PaymentStatus.Capturing || p.Status == PaymentStatus.Voiding) && p.UpdatedAt < staleBefore)
                || p.Refunds.Any(r => r.Status == RefundStatus.Requested && (r.OutcomeUnknownSince != null || r.RequestedAt < staleBefore)))
            .Include(p => p.Refunds);
    }
}

public class SavedPaymentMethodsForBuyerSpec : Specification<SavedPaymentMethod>
{
    public SavedPaymentMethodsForBuyerSpec(string buyerId, bool activeOnly = true)
    {
        Query.Where(m => m.BuyerId == buyerId);
        if (activeOnly)
        {
            Query.Where(m => m.Status == SavedPaymentMethodStatus.Active);
        }
        Query.OrderBy(m => m.Id);
    }
}

public class SavedPaymentMethodForBuyerSpec : Specification<SavedPaymentMethod>
{
    public SavedPaymentMethodForBuyerSpec(int id, string buyerId)
    {
        Query.Where(m => m.Id == id && m.BuyerId == buyerId);
    }
}

public class SavedPaymentMethodsNeedingSettlementSpec : Specification<SavedPaymentMethod>
{
    public SavedPaymentMethodsNeedingSettlementSpec(DateTimeOffset staleBefore)
    {
        Query.Where(m =>
            (m.Status == SavedPaymentMethodStatus.Saving && (m.OutcomeUnknownSince != null || m.CreatedAt < staleBefore))
            || (m.Status == SavedPaymentMethodStatus.Deleted && !m.VaultTokenRemoved && m.DeletedAt < staleBefore));
    }
}
