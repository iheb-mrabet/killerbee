using Microsoft.EntityFrameworkCore;

namespace KillerBee.Data;

public static class Seed
{
    public static async Task Demo(KillerBeeDb db)
    {
        await db.Database.EnsureCreatedAsync();
        foreach (var sql in KillerBeeDb.Views(true).Split("GO", StringSplitOptions.RemoveEmptyEntries))
            await db.Database.ExecuteSqlRawAsync(sql.Replace("CREATE VIEW ", "CREATE VIEW IF NOT EXISTS ", StringComparison.Ordinal));
        if (await db.Models.AnyAsync()) return;
        var resin = new Ingredient { Name = "Résine biosourcée", NormalizedName = "RÉSINE BIOSOURCÉE", Description = "Ingrédient fictif utilisé pour la démonstration." };
        var pigment = new Ingredient { Name = "Pigment violet", NormalizedName = "PIGMENT VIOLET", Description = "Colorant fictif de la gamme KillerBee." };
        var diameter = new CharacteristicDefinition { Name = "Diamètre", NormalizedName = "DIAMÈTRE", Kind = "number", Unit = "cm", Required = true };
        var leisure = new ProductModel { Name = "KB Loisir", NormalizedName = "KB LOISIR", Description = "Modèle de démonstration validé pour le loisir.", Range = "Loisir", UnitPriceCents = 1290, State = "Validated" };
        var competition = new ProductModel { Name = "KB Competition", NormalizedName = "KB COMPETITION", Description = "Prototype de démonstration en cours de conception.", Range = "Sport", UnitPriceCents = 2490 };
        foreach (var m in new[] { leisure, competition })
        {
            m.Ingredients.AddRange([new() { IngredientId = resin.Id, QuantityCg = 17337 }, new() { IngredientId = pigment.Id, QuantityCg = 163 }]);
            m.Characteristics.Add(new() { CharacteristicId = diameter.Id, Value = "27.3" });
        }
        var process = new ManufacturingProcess { ModelId = leisure.Id, Name = "Moulage standard", NormalizedName = "MOULAGE STANDARD", Description = "Mode opératoire fictif pour les démonstrations." };
        var step = new ProcessStep { Name = "Préparer le mélange", Description = "Peser les ingrédients selon la composition validée.", Position = 1 };
        process.Steps.Add(step);
        process.Steps.Add(new() { Name = "Contrôler le produit", Description = "Effectuer les contrôles prévus avant fabrication.", Position = 2 });
        var test = new TestDefinition { ProcessId = process.Id, StepId = step.Id, Name = "Contrôle de masse", Description = "Contrôle fictif.", ExpectedResult = "175,00 g" };
        db.Ingredients.AddRange(resin, pigment); db.Definitions.Add(diameter);
        db.Models.AddRange(leisure, competition); db.Processes.Add(process); db.Tests.Add(test);
        db.Executions.Add(new() { TestId = test.Id, ModelRevision = 1, Outcome = "Passed", Observations = "175,00 g — donnée fictive", ExecutedBy = "demo.test", ExecutedAt = DateTimeOffset.UtcNow, Validation = "Approved", ValidatedBy = "demo.test", ValidatedAt = DateTimeOffset.UtcNow });
        await db.SaveChangesAsync();
    }
}
