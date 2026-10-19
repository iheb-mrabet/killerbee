using KillerBee.Core;
using Microsoft.EntityFrameworkCore;

namespace KillerBee.Data;

public sealed class TestPlugin : IBusinessPlugin
{
    public bool Handles(string op) => op.StartsWith("tests.", StringComparison.Ordinal);
    public async Task<object?> Execute(WorkContext c)
    {
        var db = c.Db;
        switch (c.Request.Operation)
        {
            case "tests.list":
            {
                var q = c.Input<ListQuery>(); var search = Validation.Text(q.Search, "search", 120, false).ToUpperInvariant();
                var tests = await (from t in db.Tests join p in db.Processes on t.ProcessId equals p.Id join m in db.Models on p.ModelId equals m.Id
                    where (q.ModelId == null || p.ModelId == q.ModelId) && t.Name.ToUpper().Contains(search)
                    orderby t.Name select new { t.Id, t.ProcessId, ProcessName = p.Name, t.StepId, t.Name, t.Description, t.ExpectedResult, t.Revision,
                        ModelId = m.Id, ModelName = m.Name, ModelState = m.State, ModelRevision = m.Revision }).Take(500).ToListAsync();
                var ids = tests.Select(t => t.Id).ToArray();
                var executions = await db.Executions.Where(e => ids.Contains(e.TestId)).ToListAsync();
                return tests.Select(t => new { t.Id, t.ProcessId, t.ProcessName, t.StepId, t.Name, t.Description, t.ExpectedResult, t.Revision,
                    t.ModelId, t.ModelName, t.ModelState, t.ModelRevision,
                    Executions = executions.Where(e => e.TestId == t.Id).OrderByDescending(e => e.ExecutedAt).Take(30).Select(e => new {
                        e.Id, e.TestId, e.ModelRevision, e.Outcome, e.Observations, e.ExecutedBy, e.ExecutedAt,
                        e.Validation, e.ValidatedBy, e.ValidatedAt, e.ValidationComment, Current = e.ModelRevision == t.ModelRevision }) }).ToArray();
            }
            case "tests.save":
            {
                var input = c.Input<TestInput>(); var process = await c.Process(input.ProcessId);
                Validation.Require(input.StepId == null || process.Steps.Any(s => s.Id == input.StepId), "Cette étape n’appartient pas au procédé.");
                var test = input.Id is { } id ? await db.Tests.FindAsync(id)
                    ?? throw new BusinessException(404, "NOT_FOUND", "Test introuvable.") : new TestDefinition { ProcessId = process.Id };
                if (input.Id is not null)
                {
                    Validation.Revision(input.Revision, test.Revision);
                    Validation.Require(test.ProcessId == input.ProcessId, "Un test existant ne peut pas changer de procédé.");
                    test.Revision++;
                }
                else db.Tests.Add(test);
                test.Name = Validation.Text(input.Name, "name", 120); test.Description = Validation.Text(input.Description, "description", 2000);
                test.ExpectedResult = Validation.Text(input.ExpectedResult, "expectedResult", 2000); test.StepId = input.StepId;
                WorkContext.Invalidate(await c.Model(process.ModelId)); c.Audit(test.Id.ToString()); return new { test.Id, test.Revision };
            }
            case "tests.delete":
            {
                var input = c.Input<IdRequest>(); var test = await db.Tests.FindAsync(input.Id)
                    ?? throw new BusinessException(404, "NOT_FOUND", "Test introuvable.");
                Validation.Revision(input.Revision, test.Revision);
                Validation.Require(!await db.Executions.AnyAsync(e => e.TestId == test.Id), "Ce test possède un historique. Sa suppression est bloquée pour conserver la traçabilité.", "HISTORY_PROTECTED", 409);
                var process = await c.Process(test.ProcessId); WorkContext.Invalidate(await c.Model(process.ModelId));
                db.Tests.Remove(test); c.Audit(test.Id.ToString()); return new { deleted = true };
            }
            case "tests.execute":
            {
                var input = c.Input<ExecutionInput>(); var test = await db.Tests.FindAsync(input.TestId)
                    ?? throw new BusinessException(404, "NOT_FOUND", "Test introuvable.");
                var process = await c.Process(test.ProcessId); var model = await c.Model(process.ModelId);
                Validation.Require(model.State == "InTesting", "Le modèle doit être soumis aux tests avant leur exécution.", "STATE_CONFLICT", 409);
                Validation.Require(input.Outcome is "Passed" or "Failed", "Résultat attendu : Passed ou Failed.");
                var execution = new TestExecution { TestId = test.Id, ModelRevision = model.Revision, Outcome = input.Outcome,
                    Observations = Validation.Text(input.Observations, "observations", 2000), ExecutedBy = c.User.Identity!.Name!, ExecutedAt = DateTimeOffset.UtcNow };
                db.Executions.Add(execution); c.Audit(execution.Id.ToString()); return execution;
            }
            case "tests.validate":
            {
                var input = c.Input<ValidationInput>(); var execution = await db.Executions.FindAsync(input.ExecutionId)
                    ?? throw new BusinessException(404, "NOT_FOUND", "Exécution introuvable.");
                var test = (await db.Tests.FindAsync(execution.TestId))!; var process = await c.Process(test.ProcessId);
                var model = await c.Model(process.ModelId);
                Validation.Require(model.State == "InTesting" && execution.ModelRevision == model.Revision,
                    "La recette ou le procédé a changé. Exécutez à nouveau le test sur la version actuelle.", "STALE_TEST", 409);
                Validation.Require(execution.Validation == "Pending", "Cette exécution a déjà été validée ou refusée.", "ALREADY_VALIDATED", 409);
                Validation.Require(!input.Approved || execution.Outcome == "Passed", "Une exécution échouée ne peut pas être approuvée.");
                var currentExecutions = await db.Executions.Where(e => e.TestId == test.Id && e.ModelRevision == model.Revision).ToListAsync();
                Validation.Require(currentExecutions.OrderByDescending(e => e.ExecutedAt).First().Id == execution.Id,
                    "Une exécution plus récente existe pour ce test.", "STALE_TEST", 409);
                execution.Validation = input.Approved ? "Approved" : "Rejected"; execution.ValidatedBy = c.User.Identity!.Name!;
                execution.ValidatedAt = DateTimeOffset.UtcNow; execution.ValidationComment = Validation.Text(input.Comment, "comment", 2000);
                await db.SaveChangesAsync();
                var processIds = await db.Processes.Where(p => p.ModelId == model.Id).Select(p => p.Id).ToListAsync();
                var testIds = await db.Tests.Where(t => processIds.Contains(t.ProcessId)).Select(t => t.Id).ToListAsync();
                var all = await db.Executions.Where(e => testIds.Contains(e.TestId) && e.ModelRevision == model.Revision).ToListAsync();
                if (testIds.Count > 0 && testIds.All(id => all.Where(e => e.TestId == id).OrderByDescending(e => e.ExecutedAt).FirstOrDefault()
                    is { Outcome: "Passed", Validation: "Approved" })) model.State = "Validated";
                c.Audit(execution.Id.ToString()); return new { execution.Id, execution.Validation, ModelId = model.Id, ModelState = model.State };
            }
            default: throw new BusinessException(404, "OPERATION_UNKNOWN", "Opération inconnue.");
        }
    }
}
