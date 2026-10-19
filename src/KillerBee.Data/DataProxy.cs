using System.Data;
using System.Security.Claims;
using System.Security.Principal;
using KillerBee.Core;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace KillerBee.Data;

public sealed class DataProxy(DbContextOptions<KillerBeeDb> options, bool demo)
{
    public async Task<T> Run<T>(ClaimsPrincipal user, Func<KillerBeeDb, Task<T>> work)
    {
        async Task<T> Transaction()
        {
            await using var db = new KillerBeeDb(options);
            await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable);
            try
            {
                var result = await work(db);
                await db.SaveChangesAsync();
                await transaction.CommitAsync();
                return result;
            }
            catch (DbUpdateConcurrencyException)
            {
                throw new BusinessException(409, "REVISION_CONFLICT", "Modification concurrente détectée. Rechargez les données.");
            }
            catch (DbUpdateException)
            {
                throw new BusinessException(409, "DATA_CONFLICT", "Une contrainte de données empêche cette opération. Vérifiez les doublons et les références.");
            }
        }
        if (demo) return await Transaction();
        if (!OperatingSystem.IsWindows()) throw new InvalidOperationException("Windows identity required.");
        var identity = user.Identities.OfType<WindowsIdentity>().FirstOrDefault()
            ?? throw new BusinessException(401, "WINDOWS_IDENTITY_REQUIRED", "Identité Windows nécessaire pour SQL Server.");
        // Context creation, opening the connection, queries and commit all run as the caller, not the service account.
        return await WindowsIdentity.RunImpersonatedAsync(identity.AccessToken, Transaction);
    }

    public static void ValidateConnection(string connection)
    {
        var builder = new SqlConnectionStringBuilder(connection);
        if (!builder.IntegratedSecurity || !string.IsNullOrEmpty(builder.UserID) || !string.IsNullOrEmpty(builder.Password)
            || builder.TrustServerCertificate || builder.Encrypt == SqlConnectionEncryptOption.Optional)
            throw new InvalidOperationException("SQL requires Integrated Security, encryption and trusted certificate validation, without shared SQL credentials.");
    }
}
