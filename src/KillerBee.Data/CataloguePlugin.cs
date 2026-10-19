using System.Globalization;
using KillerBee.Core;
using Microsoft.EntityFrameworkCore;

namespace KillerBee.Data;

public sealed class CataloguePlugin : IBusinessPlugin
{
    public bool Handles(string op) => op.StartsWith("models.", StringComparison.Ordinal)
        || op.StartsWith("ingredients.", StringComparison.Ordinal) || op.StartsWith("characteristics.", StringComparison.Ordinal);

    public async Task<object?> Execute(WorkContext c)
    {
        var db = c.Db;
        switch (c.Request.Operation)
        {
            case "models.list":
            {
                var q = c.Input<ListQuery>();
                var search = Validation.Text(q.Search, "search", 120, false).ToUpperInvariant();
                var range = Validation.Text(q.Range, "range", 80, false);
                if (Roles.ProductionOnly(c.User))
                    return await db.ProductionModels.Where(m => m.Name.ToUpper().Contains(search) && (range == "" || m.Range == range))
                        .OrderBy(m => m.Name).Take(500).Select(m => new { m.Id, m.Name, m.Description, m.UnitPriceCents, m.Range, m.Revision, State = "Validated" }).ToListAsync();
                return await db.Models.Where(m => m.NormalizedName.Contains(search) && (range == "" || m.Range == range))
                    .OrderBy(m => m.Name).Take(500).Select(m => new { m.Id, m.Name, m.Description, m.UnitPriceCents, m.Range, m.Revision, m.State }).ToListAsync();
            }
            case "models.get": return await ReadModel(c, c.Input<IdRequest>().Id);
            case "models.save":
            {
                var input = c.Input<ModelInput>();
                var name = Validation.Text(input.Name, "name", 120);
                var description = Validation.Text(input.Description, "description", 2000);
                var range = Validation.Text(input.Range, "range", 80);
                Validation.Require(input.UnitPriceCents is >= 0 and <= 100000000, "Prix invalide (centimes entiers attendus).");
                Validation.Require(input.Ingredients is { Count: > 0 and <= 100 }, "Renseignez entre 1 et 100 ingrédients.");
                var ingredients = input.Ingredients!;
                Validation.Require(ingredients.All(i => i.QuantityCg is > 0 and <= 100000000), "Les quantités doivent être des centigrammes entiers positifs.");
                Validation.Require(ingredients.Select(i => i.IngredientId).Distinct().Count() == ingredients.Count, "Un ingrédient ne peut apparaître qu’une fois dans une composition.");
                var ingredientIds = ingredients.Select(i => i.IngredientId).ToArray();
                Validation.Require(await db.Ingredients.CountAsync(i => ingredientIds.Contains(i.Id)) == ingredients.Count, "Un ingrédient est introuvable.");
                ProductModel model;
                if (input.Id is { } id)
                {
                    model = await db.Models.Include(m => m.Ingredients).Include(m => m.Characteristics).SingleOrDefaultAsync(m => m.Id == id)
                        ?? throw new BusinessException(404, "NOT_FOUND", "Modèle introuvable.");
                    Validation.Revision(input.Revision, model.Revision);
                }
                else { model = new(); db.Models.Add(model); }
                Validation.Require(!await db.Models.AnyAsync(m => m.Id != model.Id && m.NormalizedName == name.ToUpper()), "Ce nom de modèle existe déjà.", "DUPLICATE", 409);
                var defs = await db.Definitions.Where(d => d.ModelId == null || d.ModelId == model.Id).ToListAsync();
                var values = input.Characteristics ?? new();
                Validation.Require(values.Count <= 200 && values.Keys.All(k => defs.Any(d => d.Id == k)), "Caractéristique inconnue ou non applicable au modèle.");
                foreach (var d in defs)
                {
                    var v = values.GetValueOrDefault(d.Id)?.Trim() ?? "";
                    Validation.Require(!d.Required || v.Length > 0, $"La caractéristique « {d.Name} » est obligatoire.");
                    if (v.Length > 0) ValidateValue(d, v);
                }
                model.Name = name; model.NormalizedName = name.ToUpperInvariant(); model.Description = description;
                model.UnitPriceCents = input.UnitPriceCents; model.Range = range;
                if (input.Id is not null) WorkContext.Invalidate(model);
                foreach (var old in model.Ingredients.ToList())
                    if (!ingredientIds.Contains(old.IngredientId)) { db.Compositions.Remove(old); model.Ingredients.Remove(old); }
                foreach (var i in ingredients)
                {
                    var row = model.Ingredients.SingleOrDefault(x => x.IngredientId == i.IngredientId);
                    if (row is null) model.Ingredients.Add(new() { ModelId = model.Id, IngredientId = i.IngredientId, QuantityCg = i.QuantityCg });
                    else row.QuantityCg = i.QuantityCg;
                }
                foreach (var old in model.Characteristics.ToList())
                    if (!values.TryGetValue(old.CharacteristicId, out var v) || string.IsNullOrWhiteSpace(v)) { db.Values.Remove(old); model.Characteristics.Remove(old); }
                foreach (var (key, value) in values.Where(kv => !string.IsNullOrWhiteSpace(kv.Value)))
                {
                    var row = model.Characteristics.SingleOrDefault(x => x.CharacteristicId == key);
                    if (row is null) model.Characteristics.Add(new() { ModelId = model.Id, CharacteristicId = key, Value = value.Trim() });
                    else row.Value = value.Trim();
                }
                c.Audit(model.Id.ToString()); await db.SaveChangesAsync();
                return await ReadModel(c, model.Id);
            }
            case "models.submit":
            {
                var input = c.Input<IdRequest>(); var model = await c.Model(input.Id);
                Validation.Revision(input.Revision, model.Revision);
                Validation.Require(model.State == "Draft", "Ce modèle n’est plus un brouillon.", "STATE_CONFLICT", 409);
                var definitions = await db.Definitions.Where(d => d.ModelId == null || d.ModelId == model.Id).ToListAsync();
                var values = await db.Values.Where(v => v.ModelId == model.Id).ToListAsync();
                foreach (var d in definitions)
                {
                    var v = values.SingleOrDefault(x => x.CharacteristicId == d.Id)?.Value ?? "";
                    Validation.Require(!d.Required || v.Length > 0, $"Renseignez la caractéristique « {d.Name} ».");
                    if (v.Length > 0) ValidateValue(d, v);
                }
                var processes = await db.Processes.Include(p => p.Steps).Where(p => p.ModelId == model.Id).ToListAsync();
                Validation.Require(processes.Count > 0 && processes.All(p => p.Steps.Count > 0), "Ajoutez un procédé avec ses étapes avant de soumettre le modèle.");
                var ids = processes.Select(p => p.Id).ToArray();
                var tests = await db.Tests.Where(t => ids.Contains(t.ProcessId)).ToListAsync();
                Validation.Require(processes.All(p => tests.Any(t => t.ProcessId == p.Id)), "Chaque procédé doit avoir au moins un test défini.");
                model.State = "InTesting"; c.Audit(model.Id.ToString()); return new { model.Id, model.State, model.Revision };
            }
            case "models.delete":
            {
                var input = c.Input<IdRequest>(); var model = await c.Model(input.Id);
                Validation.Revision(input.Revision, model.Revision);
                Validation.Require(!await db.Processes.AnyAsync(p => p.ModelId == model.Id), "Supprimez les procédés du modèle avant sa suppression.", "REFERENCED", 409);
                var values = await db.Values.Where(v => v.ModelId == model.Id).ToListAsync(); db.Values.RemoveRange(values);
                db.Compositions.RemoveRange(await db.Compositions.Where(i => i.ModelId == model.Id).ToListAsync());
                db.Definitions.RemoveRange(await db.Definitions.Where(d => d.ModelId == model.Id).ToListAsync());
                db.Models.Remove(model); c.Audit(model.Id.ToString()); return new { deleted = true };
            }
            case "ingredients.list":
            {
                var q = c.Input<ListQuery>(); var search = Validation.Text(q.Search, "search", 120, false).ToUpperInvariant();
                return await db.Ingredients.Where(i => i.NormalizedName.Contains(search)).OrderBy(i => i.Name).Take(500)
                    .Select(i => new { i.Id, i.Name, i.Description, i.Revision }).ToListAsync();
            }
            case "ingredients.save":
            {
                var input = c.Input<IngredientInput>(); var name = Validation.Text(input.Name, "name", 120);
                var ingredient = input.Id is { } id ? await db.Ingredients.FindAsync(id)
                    ?? throw new BusinessException(404, "NOT_FOUND", "Ingrédient introuvable.") : new Ingredient();
                if (input.Id is not null) Validation.Revision(input.Revision, ingredient.Revision);
                Validation.Require(!await db.Ingredients.AnyAsync(i => i.Id != ingredient.Id && i.NormalizedName == name.ToUpper()), "Ce nom d’ingrédient existe déjà.", "DUPLICATE", 409);
                ingredient.Name = name; ingredient.NormalizedName = name.ToUpperInvariant(); ingredient.Description = Validation.Text(input.Description, "description", 2000);
                if (input.Id is null) db.Ingredients.Add(ingredient);
                else
                {
                    ingredient.Revision++;
                    var ids = await db.Compositions.Where(i => i.IngredientId == ingredient.Id).Select(i => i.ModelId).ToListAsync();
                    foreach (var m in await db.Models.Where(m => ids.Contains(m.Id)).ToListAsync()) WorkContext.Invalidate(m);
                }
                c.Audit(ingredient.Id.ToString()); return new { ingredient.Id, ingredient.Name, ingredient.Description, ingredient.Revision };
            }
            case "ingredients.delete":
            {
                var input = c.Input<IdRequest>(); var ingredient = await db.Ingredients.FindAsync(input.Id)
                    ?? throw new BusinessException(404, "NOT_FOUND", "Ingrédient introuvable.");
                Validation.Revision(input.Revision, ingredient.Revision);
                Validation.Require(!await db.Compositions.AnyAsync(i => i.IngredientId == ingredient.Id), "Cet ingrédient est utilisé dans une composition.", "REFERENCED", 409);
                db.Ingredients.Remove(ingredient); c.Audit(ingredient.Id.ToString()); return new { deleted = true };
            }
            case "characteristics.list":
            {
                var q = c.Input<ListQuery>();
                return await db.Definitions.Where(d => q.ModelId == null || d.ModelId == null || d.ModelId == q.ModelId)
                    .OrderBy(d => d.Name).Take(500).Select(d => new { d.Id, d.Name, d.Kind, d.Unit, d.Required, d.ModelId, d.Revision }).ToListAsync();
            }
            case "characteristics.save":
            {
                var input = c.Input<CharacteristicInput>(); var name = Validation.Text(input.Name, "name", 120);
                Validation.Require(input.Kind is "text" or "number" or "boolean", "Type de caractéristique invalide.");
                if (input.ModelId is { } mid) _ = await c.Model(mid);
                var definition = input.Id is { } id ? await db.Definitions.FindAsync(id)
                    ?? throw new BusinessException(404, "NOT_FOUND", "Caractéristique introuvable.") : new CharacteristicDefinition();
                if (input.Id is not null)
                {
                    Validation.Revision(input.Revision, definition.Revision);
                    Validation.Require(definition.ModelId == input.ModelId, "La portée d’une caractéristique existante ne peut pas changer.");
                    foreach (var v in await db.Values.Where(v => v.CharacteristicId == definition.Id).ToListAsync())
                        ValidateValue(new() { Name = name, Kind = input.Kind }, v.Value);
                    definition.Revision++;
                }
                else db.Definitions.Add(definition);
                var scope = input.ModelId?.ToString() ?? "common";
                Validation.Require(!await db.Definitions.AnyAsync(d => d.Id != definition.Id && d.Scope == scope && d.NormalizedName == name.ToUpper()), "Cette caractéristique existe déjà dans cette portée.", "DUPLICATE", 409);
                definition.Name = name; definition.NormalizedName = name.ToUpperInvariant(); definition.Kind = input.Kind;
                definition.Unit = Validation.Text(input.Unit, "unit", 40, false); definition.Required = input.Required;
                definition.ModelId = input.ModelId; definition.Scope = scope;
                foreach (var m in await db.Models.Where(m => input.ModelId == null || m.Id == input.ModelId).ToListAsync()) WorkContext.Invalidate(m);
                c.Audit(definition.Id.ToString()); return new { definition.Id, definition.Name, definition.Kind, definition.Unit, definition.Required, definition.ModelId, definition.Revision };
            }
            case "characteristics.delete":
            {
                var input = c.Input<IdRequest>(); var definition = await db.Definitions.FindAsync(input.Id)
                    ?? throw new BusinessException(404, "NOT_FOUND", "Caractéristique introuvable.");
                Validation.Revision(input.Revision, definition.Revision);
                db.Values.RemoveRange(await db.Values.Where(v => v.CharacteristicId == definition.Id).ToListAsync());
                foreach (var m in await db.Models.Where(m => definition.ModelId == null || m.Id == definition.ModelId).ToListAsync()) WorkContext.Invalidate(m);
                db.Definitions.Remove(definition); c.Audit(definition.Id.ToString()); return new { deleted = true };
            }
            default: throw new BusinessException(404, "OPERATION_UNKNOWN", "Opération inconnue.");
        }
    }

    public static async Task<object> ReadModel(WorkContext c, Guid id)
    {
        var db = c.Db;
        if (Roles.ProductionOnly(c.User))
        {
            var m = await db.ProductionModels.SingleOrDefaultAsync(m => m.Id == id)
                ?? throw new BusinessException(404, "NOT_FOUND", "Modèle introuvable ou indisponible pour la production.");
            return new { m.Id, m.Name, m.Description, m.UnitPriceCents, m.Range, m.Revision, State = "Validated",
                Ingredients = await db.ProductionRecipes.Where(i => i.ModelId == id).ToListAsync(),
                Characteristics = await db.ProductionCharacteristics.Where(v => v.ModelId == id).ToListAsync() };
        }
        var model = await c.Model(id);
        var ingredients = await (from i in db.Compositions join d in db.Ingredients on i.IngredientId equals d.Id where i.ModelId == id
            select new { i.IngredientId, d.Name, i.QuantityCg }).ToListAsync();
        var values = await (from v in db.Values join d in db.Definitions on v.CharacteristicId equals d.Id where v.ModelId == id
            select new { v.CharacteristicId, d.Name, d.Kind, d.Unit, v.Value }).ToListAsync();
        return new { model.Id, model.Name, model.Description, model.UnitPriceCents, model.Range, model.Revision, model.State,
            Ingredients = ingredients, Characteristics = values };
    }

    private static void ValidateValue(CharacteristicDefinition d, string value)
    {
        Validation.Text(value, d.Name, 500);
        if (d.Kind == "number") Validation.Require(decimal.TryParse(value, NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint,
            CultureInfo.InvariantCulture, out _), $"La caractéristique « {d.Name} » attend un nombre décimal (point comme séparateur).");
        if (d.Kind == "boolean") Validation.Require(value is "true" or "false", $"La caractéristique « {d.Name} » attend true ou false.");
    }
}
