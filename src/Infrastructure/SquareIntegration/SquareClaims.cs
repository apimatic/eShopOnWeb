using System;
using Microsoft.EntityFrameworkCore;

namespace Microsoft.eShopWeb.Infrastructure.SquareIntegration;

public static class SquareClaims
{
    /// <summary>
    /// True when saving a claim failed because the same key is already stored. SQL Server reports a primary-key
    /// violation as <see cref="DbUpdateException"/>; the in-memory provider reports it as <see cref="ArgumentException"/>.
    /// </summary>
    public static bool IsDuplicateKey(Exception exception) =>
        exception is DbUpdateException and not DbUpdateConcurrencyException
        || (exception is ArgumentException
            && exception.StackTrace?.Contains("Microsoft.EntityFrameworkCore.InMemory.Storage.Internal.InMemoryTable", StringComparison.Ordinal) == true);
}
