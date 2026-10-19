namespace KillerBee.Data;

public abstract class Versioned
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public int Revision { get; set; } = 1;
}
public sealed class Ingredient : Versioned
{
    public string Name { get; set; } = "";
    public string NormalizedName { get; set; } = "";
    public string Description { get; set; } = "";
}
public sealed class ProductModel : Versioned
{
    public string Name { get; set; } = "";
    public string NormalizedName { get; set; } = "";
    public string Description { get; set; } = "";
    public long UnitPriceCents { get; set; }
    public string Range { get; set; } = "";
    public string State { get; set; } = "Draft";
    public List<Composition> Ingredients { get; set; } = [];
    public List<CharacteristicValue> Characteristics { get; set; } = [];
}
public sealed class Composition
{
    public Guid ModelId { get; set; }
    public Guid IngredientId { get; set; }
    public long QuantityCg { get; set; }
}
public sealed class CharacteristicDefinition : Versioned
{
    public string Name { get; set; } = "";
    public string NormalizedName { get; set; } = "";
    public string Kind { get; set; } = "text";
    public string Unit { get; set; } = "";
    public bool Required { get; set; }
    public Guid? ModelId { get; set; }
    public string Scope { get; set; } = "common";
}
public sealed class CharacteristicValue
{
    public Guid ModelId { get; set; }
    public Guid CharacteristicId { get; set; }
    public string Value { get; set; } = "";
}
public sealed class ManufacturingProcess : Versioned
{
    public Guid ModelId { get; set; }
    public string Name { get; set; } = "";
    public string NormalizedName { get; set; } = "";
    public string Description { get; set; } = "";
    public List<ProcessStep> Steps { get; set; } = [];
}
public sealed class ProcessStep
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid ProcessId { get; set; }
    public int Position { get; set; }
    public string Name { get; set; } = "";
    public string Description { get; set; } = "";
}
public sealed class TestDefinition : Versioned
{
    public Guid ProcessId { get; set; }
    public Guid? StepId { get; set; }
    public string Name { get; set; } = "";
    public string Description { get; set; } = "";
    public string ExpectedResult { get; set; } = "";
}
public sealed class TestExecution
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid TestId { get; set; }
    public int ModelRevision { get; set; }
    public string Outcome { get; set; } = "";
    public string Observations { get; set; } = "";
    public string ExecutedBy { get; set; } = "";
    public DateTimeOffset ExecutedAt { get; set; }
    public string Validation { get; set; } = "Pending";
    public string? ValidatedBy { get; set; }
    public DateTimeOffset? ValidatedAt { get; set; }
    public string ValidationComment { get; set; } = "";
}
public sealed class AuditEvent
{
    public long Id { get; set; }
    public DateTimeOffset At { get; set; }
    public string Actor { get; set; } = "";
    public string Operation { get; set; } = "";
    public string EntityId { get; set; } = "";
    public string CorrelationId { get; set; } = "";
}

// Read-only views deliberately expose only validated product information to SQL production users.
public sealed class ProductionModel
{
    public Guid Id { get; set; }
    public int Revision { get; set; }
    public string Name { get; set; } = "";
    public string Description { get; set; } = "";
    public long UnitPriceCents { get; set; }
    public string Range { get; set; } = "";
}
public sealed class ProductionRecipe
{
    public Guid ModelId { get; set; }
    public Guid IngredientId { get; set; }
    public string Name { get; set; } = "";
    public long QuantityCg { get; set; }
}
public sealed class ProductionCharacteristic
{
    public Guid ModelId { get; set; }
    public Guid CharacteristicId { get; set; }
    public string Name { get; set; } = "";
    public string Kind { get; set; } = "";
    public string Unit { get; set; } = "";
    public string Value { get; set; } = "";
}
public sealed class ProductionProcess
{
    public Guid Id { get; set; }
    public Guid ModelId { get; set; }
    public int Revision { get; set; }
    public string Name { get; set; } = "";
    public string Description { get; set; } = "";
}
public sealed class ProductionStep
{
    public Guid Id { get; set; }
    public Guid ProcessId { get; set; }
    public int Position { get; set; }
    public string Name { get; set; } = "";
    public string Description { get; set; } = "";
}
