using System;
using Microsoft.EntityFrameworkCore;

namespace Microsoft.eShopWeb.Infrastructure.Billing;

internal static class ClaimStore
{
    /// <summary>
    /// True when inserting a claim was refused because its primary key already exists. SQL Server reports this as a
    /// <see cref="DbUpdateException"/>; the EF Core in-memory provider (UseOnlyInMemoryDatabase) throws an
    /// <see cref="ArgumentException"/> ("An item with the same key has already been added") from inside its store lock.
    /// Only call this around the SaveChanges of a single claim insert.
    /// </summary>
    public static bool IsDuplicateKey(Exception exception) =>
        exception is DbUpdateException and not DbUpdateConcurrencyException
        || exception is ArgumentException;
}
