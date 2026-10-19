using System.Security.Claims;
using System.Text.Json;

namespace KillerBee.Core;

public static class JsonDefaults
{
    public static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web);
}

public sealed record ClientInfo(string Kind = "killerbee-web", string Version = "1.0");
public sealed record DispatchRequest(Guid RequestId, ClientInfo Client, string Operation, JsonElement Payload);
public sealed record ApiReply(string Status, string Code, string Message, object? Data,
    string CorrelationId, Dictionary<string, string[]>? Errors = null)
{
    public static ApiReply Success(object? data, string id, string code = "OK", string message = "Opération réussie.")
        => new("success", code, message, data, id);
    public static ApiReply Failure(string code, string message, string id, Dictionary<string, string[]>? errors = null)
        => new("failure", code, message, null, id, errors);
}

public sealed class BusinessException(int status, string code, string message,
    Dictionary<string, string[]>? errors = null) : Exception(message)
{
    public int Status { get; } = status;
    public string Code { get; } = code;
    public Dictionary<string, string[]>? Errors { get; } = errors;
}

public static class Roles
{
    public const string Research = "RD";
    public const string Testing = "TEST";
    public const string Production = "PROD";
    public static readonly string[] All = [Research, Testing, Production];
    public static bool ProductionOnly(ClaimsPrincipal user) => user.IsInRole(Production)
        && !user.IsInRole(Research) && !user.IsInRole(Testing);
}

public static class Operations
{
    // This registry is shared by resolution and execution. No arbitrary route/SQL can be supplied by a client.
    public static readonly IReadOnlyDictionary<string, string[]> Permissions = new Dictionary<string, string[]>
    {
        ["dashboard.read"] = Roles.All,
        ["models.list"] = Roles.All, ["models.get"] = Roles.All,
        ["models.save"] = [Roles.Research], ["models.delete"] = [Roles.Research],
        ["models.submit"] = [Roles.Research],
        ["ingredients.list"] = [Roles.Research, Roles.Testing],
        ["ingredients.save"] = [Roles.Research], ["ingredients.delete"] = [Roles.Research],
        ["characteristics.list"] = [Roles.Research, Roles.Testing],
        ["characteristics.save"] = [Roles.Research], ["characteristics.delete"] = [Roles.Research],
        ["processes.list"] = Roles.All, ["processes.get"] = Roles.All,
        ["processes.save"] = [Roles.Research], ["processes.delete"] = [Roles.Research],
        ["tests.list"] = [Roles.Research, Roles.Testing],
        ["tests.save"] = [Roles.Research], ["tests.delete"] = [Roles.Research],
        ["tests.execute"] = [Roles.Testing], ["tests.validate"] = [Roles.Testing],
        ["audit.list"] = [Roles.Research], ["system.identity"] = Roles.All
    };

    public static void Authorize(DispatchRequest request, ClaimsPrincipal user)
    {
        if (request.RequestId == Guid.Empty || request.Client is null || request.Client.Kind != "killerbee-web"
            || request.Client.Version != "1.0")
            throw new BusinessException(400, "CLIENT_UNSUPPORTED", "Type ou version de l’application non pris en charge.");
        if (!Permissions.TryGetValue(request.Operation ?? "", out var permitted))
            throw new BusinessException(404, "OPERATION_UNKNOWN", "Opération inconnue.");
        if (!permitted.Any(user.IsInRole))
            throw new BusinessException(403, "ACCESS_DENIED", "Votre service n’est pas autorisé à effectuer cette opération.");
    }
}

public sealed record ListQuery(string? Search = null, string? Range = null, Guid? ModelId = null);
public sealed record IdRequest(Guid Id, int Revision = 0);
public sealed record IngredientInput(Guid? Id, string Name, string Description, int Revision = 0);
public sealed record CompositionInput(Guid IngredientId, long QuantityCg);
public sealed record ModelInput(Guid? Id, string Name, string Description, long UnitPriceCents,
    string Range, List<CompositionInput> Ingredients, Dictionary<Guid, string> Characteristics, int Revision = 0);
public sealed record CharacteristicInput(Guid? Id, string Name, string Kind, string Unit,
    bool Required, Guid? ModelId, int Revision = 0);
public sealed record StepInput(Guid? Id, string Name, string Description);
public sealed record ProcessInput(Guid? Id, Guid ModelId, string Name, string Description,
    List<StepInput> Steps, int Revision = 0);
public sealed record TestInput(Guid? Id, Guid ProcessId, Guid? StepId, string Name,
    string Description, string ExpectedResult, int Revision = 0);
public sealed record ExecutionInput(Guid TestId, string Outcome, string Observations);
public sealed record ValidationInput(Guid ExecutionId, bool Approved, string Comment);

public static class Validation
{
    public static string Text(string? value, string field, int max, bool required = true)
    {
        value = value?.Trim() ?? "";
        if ((required && value.Length == 0) || value.Length > max)
            throw new BusinessException(400, "VALIDATION_FAILED", "Vérifiez les champs du formulaire.",
                new() { [field] = [$"Valeur {(required ? "obligatoire, " : "")}limitée à {max} caractères."] });
        return value;
    }
    public static void Require(bool condition, string message, string code = "VALIDATION_FAILED", int status = 400)
    {
        if (!condition) throw new BusinessException(status, code, message);
    }
    public static void Revision(int provided, int current) => Require(provided == current,
        "Cet élément a été modifié. Rechargez la page avant d’enregistrer.", "REVISION_CONFLICT", 409);
}
