using System;
using Ardalis.Specification;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentMethodAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Specifications;

public class PaymentMethodByIdSpec : Specification<PaymentMethod>
{
    public PaymentMethodByIdSpec(Guid paymentMethodId)
    {
        Query.Where(p => p.Id == paymentMethodId);
    }
}