using System.Security.Claims;
using System.Text.Json;
using KillerBee.Core;
using KillerBee.Data;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace KillerBee.Tests;

public sealed class BusinessTests : IAsyncLifetime
{
    private readonly string directory = Path.Combine(Path.GetTempPath(), "killerbee-test-" + Guid.NewGuid().ToString("N"));
    private DbContextOptions<KillerBeeDb> options = null!;
    private OperationEngine Engine() => new(new DataProxy(options, true),
        [new CataloguePlugin(), new ProcessPlugin(), new TestPlugin(), new ReportingPlugin(true, "test-instance")]);
    public async Task InitializeAsync()
    {
        Directory.CreateDirectory(directory);
        options = new DbContextOptionsBuilder<KillerBeeDb>().UseSqlite($"Data Source={Path.Combine(directory, "test.db")};Foreign Keys=True").Options;
        await using var db = new KillerBeeDb(options); await Seed.Demo(db);
    }
    public Task DisposeAsync() { Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools(); Directory.Delete(directory, true); return Task.CompletedTask; }
    private async Task<JsonElement> Call(string role, string operation, object? payload = null)
    {
        var user = new ClaimsPrincipal(new ClaimsIdentity([new(ClaimTypes.Name, "test." + role), new(ClaimTypes.Role, role)], "test"));
        var request = new DispatchRequest(Guid.NewGuid(), new(), operation, JsonSerializer.SerializeToElement(payload ?? new { }, JsonDefaults.Options));
        return JsonSerializer.SerializeToElement(await Engine().Execute(request, user), JsonDefaults.Options);
    }
    private async Task<JsonElement> NewModel(string name = "Test model")
    {
        var ingredients = await Call(Roles.Research, "ingredients.list");
        var defs = await Call(Roles.Research, "characteristics.list");
        return await Call(Roles.Research, "models.save", new { name, description = "Description de test", range = "Sport", unitPriceCents = 1999,
            ingredients = new[] { new { ingredientId = ingredients[0].GetProperty("id").GetGuid(), quantityCg = 12537 } },
            characteristics = new Dictionary<string, string> { [defs[0].GetProperty("id").GetString()!] = "27.3" } });
    }

    [Fact]
    public async Task WholeWorkflowPublishesOnlyApprovedCurrentRevision()
    {
        var model = await NewModel(); var mid = model.GetProperty("id").GetGuid();
        Assert.Equal(12537, model.GetProperty("ingredients")[0].GetProperty("quantityCg").GetInt64());
        await Assert.ThrowsAsync<BusinessException>(() => Call(Roles.Production, "models.get", new { id = mid }));
        var process = await Call(Roles.Research, "processes.save", new { modelId = mid, name = "Moulage", description = "Mode opératoire", steps = new[] { new { name = "Peser", description = "Peser précisément" }, new { name = "Mouler", description = "Suivre la fiche" } } });
        var test = await Call(Roles.Research, "tests.save", new { processId = process.GetProperty("id").GetGuid(), stepId = process.GetProperty("steps")[0].GetProperty("id").GetGuid(), name = "Masse", description = "Mesurer", expectedResult = "125,37 g" });
        model = await Call(Roles.Research, "models.get", new { id = mid });
        await Call(Roles.Research, "models.submit", new { id = mid, revision = model.GetProperty("revision").GetInt32() });
        var execution = await Call(Roles.Testing, "tests.execute", new { testId = test.GetProperty("id").GetGuid(), outcome = "Passed", observations = "125,37 g" });
        await Assert.ThrowsAsync<BusinessException>(() => Call(Roles.Research, "tests.validate", new { executionId = execution.GetProperty("id").GetGuid(), approved = true, comment = "Unauthorized" }));
        await Call(Roles.Testing, "tests.validate", new { executionId = execution.GetProperty("id").GetGuid(), approved = true, comment = "Conforme" });
        var published = await Call(Roles.Production, "models.get", new { id = mid });
        Assert.Equal("Validated", published.GetProperty("state").GetString());
        Assert.Equal(12537, published.GetProperty("ingredients")[0].GetProperty("quantityCg").GetInt64());
        Assert.NotEmpty((await Call(Roles.Research, "audit.list")).EnumerateArray());
    }

    [Fact]
    public async Task RecipeChangeInvalidatesApprovalAndRejectsOldExecution()
    {
        var models = await Call(Roles.Research, "models.list");
        var id = models.EnumerateArray().Single(m => m.GetProperty("state").GetString() == "Validated").GetProperty("id").GetGuid();
        var model = await Call(Roles.Research, "models.get", new { id });
        var oldTest = (await Call(Roles.Testing, "tests.list", new { modelId = id }))[0];
        var oldExecution = oldTest.GetProperty("executions")[0].GetProperty("id").GetGuid();
        var ingredients = model.GetProperty("ingredients").EnumerateArray().Select(i => new { ingredientId = i.GetProperty("ingredientId").GetGuid(), quantityCg = i.GetProperty("quantityCg").GetInt64() + 1 }).ToArray();
        var values = model.GetProperty("characteristics").EnumerateArray().ToDictionary(v => v.GetProperty("characteristicId").GetString()!, v => v.GetProperty("value").GetString()!);
        var updated = await Call(Roles.Research, "models.save", new { id, revision = model.GetProperty("revision").GetInt32(), name = "Recette modifiée", description = "Nouveau mélange", range = "Loisir", unitPriceCents = 1290, ingredients, characteristics = values });
        Assert.Equal("Draft", updated.GetProperty("state").GetString());
        await Assert.ThrowsAsync<BusinessException>(() => Call(Roles.Production, "models.get", new { id }));
        var exception = await Assert.ThrowsAsync<BusinessException>(() => Call(Roles.Testing, "tests.validate", new { executionId = oldExecution, approved = true, comment = "Ancien test" }));
        Assert.Equal("STALE_TEST", exception.Code);
    }

    [Fact]
    public async Task PermissionsAndReferentialIntegrityAreEnforcedInExecutionLayer()
    {
        await Assert.ThrowsAsync<BusinessException>(() => Call(Roles.Production, "ingredients.list"));
        await Assert.ThrowsAsync<BusinessException>(() => Call(Roles.Testing, "ingredients.save", new { name = "Forbidden", description = "x" }));
        var ingredient = (await Call(Roles.Research, "ingredients.list"))[0];
        var exception = await Assert.ThrowsAsync<BusinessException>(() => Call(Roles.Research, "ingredients.delete", new { id = ingredient.GetProperty("id").GetGuid(), revision = ingredient.GetProperty("revision").GetInt32() }));
        Assert.Equal("REFERENCED", exception.Code);
        var unused = await Call(Roles.Research, "ingredients.save", new { name = "Inutilisé", description = "Supprimable" });
        await Call(Roles.Research, "ingredients.delete", new { id = unused.GetProperty("id").GetGuid(), revision = 1 });
    }

    [Fact]
    public async Task UnknownDynamicFieldRollsBackEntireMutation()
    {
        var before = (await Call(Roles.Research, "models.list")).GetArrayLength();
        var ingredient = (await Call(Roles.Research, "ingredients.list"))[0].GetProperty("id").GetGuid();
        await Assert.ThrowsAsync<BusinessException>(() => Call(Roles.Research, "models.save", new { name = "Do not save", description = "Atomicité", range = "Sport", unitPriceCents = 100,
            ingredients = new[] { new { ingredientId = ingredient, quantityCg = 100 } }, characteristics = new Dictionary<string, string> { [Guid.NewGuid().ToString()] = "unknown" } }));
        Assert.Equal(before, (await Call(Roles.Research, "models.list")).GetArrayLength());
    }

    [Fact]
    public async Task StaleRevisionCannotOverwriteData()
    {
        var model = await NewModel(); var id = model.GetProperty("id").GetGuid();
        var e = await Assert.ThrowsAsync<BusinessException>(() => Call(Roles.Research, "models.delete", new { id, revision = 0 }));
        Assert.Equal("REVISION_CONFLICT", e.Code);
        Assert.Equal(id, (await Call(Roles.Research, "models.get", new { id })).GetProperty("id").GetGuid());
    }

    [Fact]
    public async Task CharacteristicsCanBeAddedWithoutChangingSchema()
    {
        var model = await NewModel(); var id = model.GetProperty("id").GetGuid();
        var definition = await Call(Roles.Research, "characteristics.save", new { name = "Champ inconnu au développement", kind = "boolean", unit = "", required = false, modelId = id });
        var definitions = await Call(Roles.Research, "characteristics.list", new { modelId = id });
        Assert.Contains(definitions.EnumerateArray(), d => d.GetProperty("id").GetGuid() == definition.GetProperty("id").GetGuid());
        Assert.Equal(2, (await Call(Roles.Research, "models.get", new { id })).GetProperty("revision").GetInt32());
    }

    [Fact]
    public void SqlConfigurationRejectsSharedCredentialsAndUnverifiedEncryption()
    {
        Assert.Throws<InvalidOperationException>(() => DataProxy.ValidateConnection("Server=localhost;Database=KillerBee;User Id=sa;Password=example;Encrypt=true"));
        Assert.Throws<InvalidOperationException>(() => DataProxy.ValidateConnection("Server=localhost;Integrated Security=true;Encrypt=true;TrustServerCertificate=true"));
        DataProxy.ValidateConnection("Server=sql.example.test;Database=KillerBee;Integrated Security=true;Encrypt=true;TrustServerCertificate=false");
        var opts = new DbContextOptionsBuilder<KillerBeeDb>().UseSqlServer("Server=localhost;Integrated Security=true").Options;
        using var db = new KillerBeeDb(opts);
        var script = db.Database.GenerateCreateScript();
        Assert.Contains("[SCH_RD]", script); Assert.Contains("[SCH_TEST]", script); Assert.DoesNotContain("[dbo]", script);
        Assert.Contains("State = 'Validated'", KillerBeeDb.Views(false));
    }
}
