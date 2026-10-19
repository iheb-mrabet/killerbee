using Microsoft.EntityFrameworkCore;

namespace KillerBee.Data;

public sealed class KillerBeeDb(DbContextOptions<KillerBeeDb> options) : DbContext(options)
{
    public DbSet<Ingredient> Ingredients => Set<Ingredient>();
    public DbSet<ProductModel> Models => Set<ProductModel>();
    public DbSet<Composition> Compositions => Set<Composition>();
    public DbSet<CharacteristicDefinition> Definitions => Set<CharacteristicDefinition>();
    public DbSet<CharacteristicValue> Values => Set<CharacteristicValue>();
    public DbSet<ManufacturingProcess> Processes => Set<ManufacturingProcess>();
    public DbSet<ProcessStep> Steps => Set<ProcessStep>();
    public DbSet<TestDefinition> Tests => Set<TestDefinition>();
    public DbSet<TestExecution> Executions => Set<TestExecution>();
    public DbSet<AuditEvent> Audit => Set<AuditEvent>();
    public DbSet<ProductionModel> ProductionModels => Set<ProductionModel>();
    public DbSet<ProductionRecipe> ProductionRecipes => Set<ProductionRecipe>();
    public DbSet<ProductionCharacteristic> ProductionCharacteristics => Set<ProductionCharacteristic>();
    public DbSet<ProductionProcess> ProductionProcesses => Set<ProductionProcess>();
    public DbSet<ProductionStep> ProductionSteps => Set<ProductionStep>();

    protected override void OnModelCreating(ModelBuilder b)
    {
        b.Entity<Ingredient>().ToTable("Ingredients", "SCH_RD");
        b.Entity<ProductModel>().ToTable("Models", "SCH_RD", t => t.HasCheckConstraint("CK_Model_Price", "UnitPriceCents >= 0 AND UnitPriceCents <= 100000000"));
        b.Entity<Composition>().ToTable("Compositions", "SCH_RD", t => t.HasCheckConstraint("CK_Quantity", "QuantityCg > 0 AND QuantityCg <= 100000000"));
        b.Entity<Composition>().HasKey(x => new { x.ModelId, x.IngredientId });
        b.Entity<Composition>().HasOne<Ingredient>().WithMany().HasForeignKey(x => x.IngredientId).OnDelete(DeleteBehavior.Restrict);
        b.Entity<ProductModel>().HasMany(x => x.Ingredients).WithOne().HasForeignKey(x => x.ModelId).OnDelete(DeleteBehavior.Restrict);
        b.Entity<CharacteristicDefinition>().ToTable("CharacteristicDefinitions", "SCH_RD");
        b.Entity<CharacteristicDefinition>().HasOne<ProductModel>().WithMany().HasForeignKey(x => x.ModelId).OnDelete(DeleteBehavior.Restrict);
        b.Entity<CharacteristicDefinition>().HasIndex(x => new { x.Scope, x.NormalizedName }).IsUnique();
        b.Entity<CharacteristicValue>().ToTable("CharacteristicValues", "SCH_RD");
        b.Entity<CharacteristicValue>().HasKey(x => new { x.ModelId, x.CharacteristicId });
        b.Entity<CharacteristicValue>().HasOne<CharacteristicDefinition>().WithMany().HasForeignKey(x => x.CharacteristicId).OnDelete(DeleteBehavior.Restrict);
        b.Entity<ProductModel>().HasMany(x => x.Characteristics).WithOne().HasForeignKey(x => x.ModelId).OnDelete(DeleteBehavior.Restrict);
        b.Entity<ManufacturingProcess>().ToTable("Processes", "SCH_RD");
        b.Entity<ManufacturingProcess>().HasOne<ProductModel>().WithMany().HasForeignKey(x => x.ModelId).OnDelete(DeleteBehavior.Restrict);
        b.Entity<ManufacturingProcess>().HasIndex(x => new { x.ModelId, x.NormalizedName }).IsUnique();
        b.Entity<ProcessStep>().ToTable("Steps", "SCH_RD");
        b.Entity<ManufacturingProcess>().HasMany(x => x.Steps).WithOne().HasForeignKey(x => x.ProcessId).OnDelete(DeleteBehavior.Restrict);
        b.Entity<TestDefinition>().ToTable("TestDefinitions", "SCH_RD");
        b.Entity<TestDefinition>().HasOne<ManufacturingProcess>().WithMany().HasForeignKey(x => x.ProcessId).OnDelete(DeleteBehavior.Restrict);
        b.Entity<TestDefinition>().HasOne<ProcessStep>().WithMany().HasForeignKey(x => x.StepId).OnDelete(DeleteBehavior.Restrict);
        b.Entity<TestExecution>().ToTable("TestExecutions", "SCH_TEST");
        b.Entity<TestExecution>().HasOne<TestDefinition>().WithMany().HasForeignKey(x => x.TestId).OnDelete(DeleteBehavior.Restrict);
        b.Entity<AuditEvent>().ToTable("AuditEvents", "SCH_TEST");
        b.Entity<AuditEvent>().HasIndex(x => x.At);
        b.Entity<ProductionModel>().HasNoKey().ToView("ValidatedModels", "SCH_PROD");
        b.Entity<ProductionRecipe>().HasNoKey().ToView("ValidatedRecipes", "SCH_PROD");
        b.Entity<ProductionCharacteristic>().HasNoKey().ToView("ValidatedCharacteristics", "SCH_PROD");
        b.Entity<ProductionProcess>().HasNoKey().ToView("ValidatedProcesses", "SCH_PROD");
        b.Entity<ProductionStep>().HasNoKey().ToView("ValidatedSteps", "SCH_PROD");
        foreach (var entity in b.Model.GetEntityTypes())
        {
            if (typeof(Versioned).IsAssignableFrom(entity.ClrType))
            {
                b.Entity(entity.ClrType).HasKey("Id");
                b.Entity(entity.ClrType).Property("Revision").IsConcurrencyToken();
            }
            foreach (var property in entity.GetProperties().Where(p => p.ClrType == typeof(string)))
                property.SetMaxLength(property.Name switch
                {
                    "Description" or "Observations" or "ExpectedResult" or "ValidationComment" => 2000,
                    "Value" => 500,
                    "Name" or "NormalizedName" => 120,
                    _ => 256
                });
        }
        b.Entity<Ingredient>().HasIndex(x => x.NormalizedName).IsUnique();
        b.Entity<ProductModel>().HasIndex(x => x.NormalizedName).IsUnique();
    }

    public static string Views(bool sqlite)
    {
        string rd = sqlite ? "" : "[SCH_RD].";
        string prod = sqlite ? "" : "[SCH_PROD].";
        return $"""
            CREATE VIEW {prod}[ValidatedModels] AS SELECT Id, Revision, Name, Description, UnitPriceCents, [Range] FROM {rd}[Models] WHERE State = 'Validated';
            GO
            CREATE VIEW {prod}[ValidatedRecipes] AS SELECT c.ModelId, c.IngredientId, i.Name, c.QuantityCg FROM {rd}[Compositions] c JOIN {rd}[Ingredients] i ON i.Id=c.IngredientId JOIN {rd}[Models] m ON m.Id=c.ModelId WHERE m.State='Validated';
            GO
            CREATE VIEW {prod}[ValidatedCharacteristics] AS SELECT v.ModelId, v.CharacteristicId, d.Name, d.Kind, d.Unit, v.Value FROM {rd}[CharacteristicValues] v JOIN {rd}[CharacteristicDefinitions] d ON d.Id=v.CharacteristicId JOIN {rd}[Models] m ON m.Id=v.ModelId WHERE m.State='Validated';
            GO
            CREATE VIEW {prod}[ValidatedProcesses] AS SELECT p.Id, p.ModelId, p.Revision, p.Name, p.Description FROM {rd}[Processes] p JOIN {rd}[Models] m ON m.Id=p.ModelId WHERE m.State='Validated';
            GO
            CREATE VIEW {prod}[ValidatedSteps] AS SELECT s.Id, s.ProcessId, s.Position, s.Name, s.Description FROM {rd}[Steps] s JOIN {rd}[Processes] p ON p.Id=s.ProcessId JOIN {rd}[Models] m ON m.Id=p.ModelId WHERE m.State='Validated';
            """;
    }
}
