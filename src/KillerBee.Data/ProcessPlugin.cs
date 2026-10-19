using KillerBee.Core;
using Microsoft.EntityFrameworkCore;

namespace KillerBee.Data;

public sealed class ProcessPlugin : IBusinessPlugin
{
    public bool Handles(string op) => op.StartsWith("processes.", StringComparison.Ordinal);
    public async Task<object?> Execute(WorkContext c)
    {
        var db = c.Db;
        switch (c.Request.Operation)
        {
            case "processes.list":
            {
                var q = c.Input<ListQuery>(); var search = Validation.Text(q.Search, "search", 120, false).ToUpperInvariant();
                if (Roles.ProductionOnly(c.User))
                    return await db.ProductionProcesses.Where(p => (q.ModelId == null || p.ModelId == q.ModelId) && p.Name.ToUpper().Contains(search))
                        .OrderBy(p => p.Name).Take(500).ToListAsync();
                return await db.Processes.Where(p => (q.ModelId == null || p.ModelId == q.ModelId) && p.NormalizedName.Contains(search))
                    .OrderBy(p => p.Name).Take(500).Select(p => new { p.Id, p.ModelId, p.Name, p.Description, p.Revision }).ToListAsync();
            }
            case "processes.get": return await Read(c, c.Input<IdRequest>().Id);
            case "processes.save":
            {
                var input = c.Input<ProcessInput>(); var model = await c.Model(input.ModelId);
                var process = input.Id is { } id ? await c.Process(id) : new ManufacturingProcess { ModelId = model.Id };
                if (input.Id is not null)
                {
                    Validation.Revision(input.Revision, process.Revision);
                    Validation.Require(process.ModelId == model.Id, "Un procédé existant ne peut pas changer de modèle.");
                    process.Revision++;
                }
                else db.Processes.Add(process);
                var name = Validation.Text(input.Name, "name", 120);
                Validation.Require(!await db.Processes.AnyAsync(p => p.Id != process.Id && p.ModelId == model.Id && p.NormalizedName == name.ToUpper()), "Ce procédé existe déjà pour ce modèle.", "DUPLICATE", 409);
                Validation.Require(input.Steps is { Count: > 0 and <= 100 }, "Renseignez entre 1 et 100 étapes.");
                var steps = input.Steps!;
                var existing = steps.Where(s => s.Id is not null).Select(s => s.Id!.Value).ToArray();
                Validation.Require(existing.Distinct().Count() == existing.Length && existing.All(id => process.Steps.Any(s => s.Id == id)), "Identifiants d’étapes invalides.");
                var removed = process.Steps.Where(s => !existing.Contains(s.Id)).ToList();
                var removedIds = removed.Select(s => s.Id).ToArray();
                Validation.Require(!await db.Tests.AnyAsync(t => t.StepId != null && removedIds.Contains(t.StepId.Value)), "Une étape supprimée est référencée par un test. Modifiez ce test d’abord.", "REFERENCED", 409);
                foreach (var old in removed) { db.Steps.Remove(old); process.Steps.Remove(old); }
                for (int i = 0; i < steps.Count; i++)
                {
                    var step = steps[i].Id is { } sid ? process.Steps.Single(s => s.Id == sid) : new ProcessStep { ProcessId = process.Id };
                    step.Name = Validation.Text(steps[i].Name, $"steps[{i}].name", 120);
                    step.Description = Validation.Text(steps[i].Description, $"steps[{i}].description", 2000);
                    step.Position = i + 1;
                    if (steps[i].Id is null) process.Steps.Add(step);
                }
                process.Name = name; process.NormalizedName = name.ToUpperInvariant(); process.Description = Validation.Text(input.Description, "description", 2000);
                WorkContext.Invalidate(model); c.Audit(process.Id.ToString()); await db.SaveChangesAsync(); return await Read(c, process.Id);
            }
            case "processes.delete":
            {
                var input = c.Input<IdRequest>(); var process = await c.Process(input.Id);
                Validation.Revision(input.Revision, process.Revision);
                Validation.Require(!await db.Tests.AnyAsync(t => t.ProcessId == process.Id), "Supprimez les définitions de tests de ce procédé avant sa suppression.", "REFERENCED", 409);
                db.Steps.RemoveRange(process.Steps); db.Processes.Remove(process);
                WorkContext.Invalidate(await c.Model(process.ModelId)); c.Audit(process.Id.ToString()); return new { deleted = true };
            }
            default: throw new BusinessException(404, "OPERATION_UNKNOWN", "Opération inconnue.");
        }
    }
    private static async Task<object> Read(WorkContext c, Guid id)
    {
        if (Roles.ProductionOnly(c.User))
        {
            var p = await c.Db.ProductionProcesses.SingleOrDefaultAsync(p => p.Id == id)
                ?? throw new BusinessException(404, "NOT_FOUND", "Procédé introuvable ou indisponible pour la production.");
            return new { p.Id, p.ModelId, p.Name, p.Description, p.Revision,
                Steps = await c.Db.ProductionSteps.Where(s => s.ProcessId == id).OrderBy(s => s.Position).ToListAsync() };
        }
        var process = await c.Process(id);
        return new { process.Id, process.ModelId, process.Name, process.Description, process.Revision,
            Steps = process.Steps.OrderBy(s => s.Position).Select(s => new { s.Id, s.Name, s.Description, s.Position }).ToArray() };
    }
}
