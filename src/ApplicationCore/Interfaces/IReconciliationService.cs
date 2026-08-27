using System;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Models.Payments;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

public interface IReconciliationService
{
    Task<ReconciliationReport> GetReconciliationAsync(DateTimeOffset from, DateTimeOffset to);
}
