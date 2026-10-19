using System.Security.Claims;
using System.Text.Json;
using KillerBee.Core;
using Microsoft.EntityFrameworkCore;

namespace KillerBee.Data;

public interface IBusinessPlugin
{
    bool Handles(string operation);
    Task<object?> Execute(WorkContext context);
}

public sealed record WorkContext(KillerBeeDb Db, ClaimsPrincipal User, DispatchRequest Request)
{
    public T Input<T>()
    {
        try { return Request.Payload.Deserialize<T>(JsonDefaults.Options) ?? throw new JsonException(); }
        catch (JsonException) { throw new BusinessException(400, "VALIDATION_FAILED", "Le format des données est invalide."); }
    }
    public void Audit(string entityId)
    {
        Db.Audit.Add(new() { At = DateTimeOffset.UtcNow, Actor = User.Identity?.Name ?? "unknown",
            Operation = Request.Operation, EntityId = entityId, CorrelationId = Request.RequestId.ToString("N") });
    }
    public async Task<ProductModel> Model(Guid id)
        => await Db.Models.SingleOrDefaultAsync(m => m.Id == id)
            ?? throw new BusinessException(404, "NOT_FOUND", "Modèle introuvable.");
    public async Task<ManufacturingProcess> Process(Guid id)
        => await Db.Processes.Include(p => p.Steps).SingleOrDefaultAsync(p => p.Id == id)
            ?? throw new BusinessException(404, "NOT_FOUND", "Procédé introuvable.");
    public static void Invalidate(ProductModel model) { model.Revision++; model.State = "Draft"; }
}

public sealed class OperationEngine(DataProxy data, IEnumerable<IBusinessPlugin> plugins)
{
    public Task<object?> Execute(DispatchRequest request, ClaimsPrincipal user)
    {
        Operations.Authorize(request, user);
        var plugin = plugins.SingleOrDefault(p => p.Handles(request.Operation))
            ?? throw new BusinessException(404, "OPERATION_UNKNOWN", "Service introuvable.");
        return data.Run(user, db => plugin.Execute(new(db, user, request)));
    }
}
