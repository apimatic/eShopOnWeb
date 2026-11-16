using Ardalis.Specification;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Specifications;

public class OrderPaymentByCaptureTransferIdSpec : Specification<OrderPayment>
{
    public OrderPaymentByCaptureTransferIdSpec(string transferId)
    {
        Query.Where(p => p.CaptureTransferId == transferId);
    }
}
