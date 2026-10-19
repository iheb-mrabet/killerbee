using KillerBee.Core;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace KillerBee.Data;

public sealed class ReportingPlugin(bool demo, string instance) : IBusinessPlugin
{
    public bool Handles(string op) => op is "dashboard.read" or "audit.list" or "system.identity";
    public async Task<object?> Execute(WorkContext c)
    {
        var db = c.Db;
        switch (c.Request.Operation)
        {
            case "dashboard.read":
                if (Roles.ProductionOnly(c.User)) return new { models = await db.ProductionModels.CountAsync(),
                    validated = await db.ProductionModels.CountAsync(), processes = await db.ProductionProcesses.CountAsync(), pending = 0, instance, demo };
                var models = await db.Models.ToListAsync();
                return new { models = models.Count, validated = models.Count(m => m.State == "Validated"),
                    processes = await db.Processes.CountAsync(),
                    pending = await (from e in db.Executions join t in db.Tests on e.TestId equals t.Id join p in db.Processes on t.ProcessId equals p.Id
                        join m in db.Models on p.ModelId equals m.Id where e.ModelRevision == m.Revision && e.Validation == "Pending" select e).CountAsync(), instance, demo };
            case "audit.list": return await db.Audit.OrderByDescending(e => e.Id).Take(200).ToListAsync();
            case "system.identity":
                string sqlIdentity = "SQLite — démonstration locale, identité AD non vérifiée";
                string? originalLogin = null;
                if (!demo)
                {
                    await using var command = db.Database.GetDbConnection().CreateCommand();
                    command.Transaction = db.Database.CurrentTransaction!.GetDbTransaction();
                    command.CommandText = "SELECT SUSER_SNAME(), ORIGINAL_LOGIN()";
                    await using var reader = await command.ExecuteReaderAsync(); await reader.ReadAsync();
                    sqlIdentity = reader.GetString(0); originalLogin = reader.GetString(1);
                }
                return new { user = c.User.Identity?.Name, roles = Roles.All.Where(c.User.IsInRole).ToArray(), sqlIdentity, originalLogin, instance, demo };
            default: throw new BusinessException(404, "OPERATION_UNKNOWN", "Opération inconnue.");
        }
    }
}
