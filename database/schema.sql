IF SCHEMA_ID(N'SCH_TEST') IS NULL EXEC(N'CREATE SCHEMA [SCH_TEST];');
GO


IF SCHEMA_ID(N'SCH_RD') IS NULL EXEC(N'CREATE SCHEMA [SCH_RD];');
GO


CREATE TABLE [SCH_TEST].[AuditEvents] (
    [Id] bigint NOT NULL IDENTITY,
    [At] datetimeoffset NOT NULL,
    [Actor] nvarchar(256) NOT NULL,
    [Operation] nvarchar(256) NOT NULL,
    [EntityId] nvarchar(256) NOT NULL,
    [CorrelationId] nvarchar(256) NOT NULL,
    CONSTRAINT [PK_AuditEvents] PRIMARY KEY ([Id])
);
GO


CREATE TABLE [SCH_RD].[Ingredients] (
    [Id] uniqueidentifier NOT NULL,
    [Name] nvarchar(120) NOT NULL,
    [NormalizedName] nvarchar(120) NOT NULL,
    [Description] nvarchar(2000) NOT NULL,
    [Revision] int NOT NULL,
    CONSTRAINT [PK_Ingredients] PRIMARY KEY ([Id])
);
GO


CREATE TABLE [SCH_RD].[Models] (
    [Id] uniqueidentifier NOT NULL,
    [Name] nvarchar(120) NOT NULL,
    [NormalizedName] nvarchar(120) NOT NULL,
    [Description] nvarchar(2000) NOT NULL,
    [UnitPriceCents] bigint NOT NULL,
    [Range] nvarchar(256) NOT NULL,
    [State] nvarchar(256) NOT NULL,
    [Revision] int NOT NULL,
    CONSTRAINT [PK_Models] PRIMARY KEY ([Id]),
    CONSTRAINT [CK_Model_Price] CHECK (UnitPriceCents >= 0 AND UnitPriceCents <= 100000000)
);
GO


CREATE TABLE [SCH_RD].[CharacteristicDefinitions] (
    [Id] uniqueidentifier NOT NULL,
    [Name] nvarchar(120) NOT NULL,
    [NormalizedName] nvarchar(120) NOT NULL,
    [Kind] nvarchar(256) NOT NULL,
    [Unit] nvarchar(256) NOT NULL,
    [Required] bit NOT NULL,
    [ModelId] uniqueidentifier NULL,
    [Scope] nvarchar(256) NOT NULL,
    [Revision] int NOT NULL,
    CONSTRAINT [PK_CharacteristicDefinitions] PRIMARY KEY ([Id]),
    CONSTRAINT [FK_CharacteristicDefinitions_Models_ModelId] FOREIGN KEY ([ModelId]) REFERENCES [SCH_RD].[Models] ([Id]) ON DELETE NO ACTION
);
GO


CREATE TABLE [SCH_RD].[Compositions] (
    [ModelId] uniqueidentifier NOT NULL,
    [IngredientId] uniqueidentifier NOT NULL,
    [QuantityCg] bigint NOT NULL,
    CONSTRAINT [PK_Compositions] PRIMARY KEY ([ModelId], [IngredientId]),
    CONSTRAINT [CK_Quantity] CHECK (QuantityCg > 0 AND QuantityCg <= 100000000),
    CONSTRAINT [FK_Compositions_Ingredients_IngredientId] FOREIGN KEY ([IngredientId]) REFERENCES [SCH_RD].[Ingredients] ([Id]) ON DELETE NO ACTION,
    CONSTRAINT [FK_Compositions_Models_ModelId] FOREIGN KEY ([ModelId]) REFERENCES [SCH_RD].[Models] ([Id]) ON DELETE NO ACTION
);
GO


CREATE TABLE [SCH_RD].[Processes] (
    [Id] uniqueidentifier NOT NULL,
    [ModelId] uniqueidentifier NOT NULL,
    [Name] nvarchar(120) NOT NULL,
    [NormalizedName] nvarchar(120) NOT NULL,
    [Description] nvarchar(2000) NOT NULL,
    [Revision] int NOT NULL,
    CONSTRAINT [PK_Processes] PRIMARY KEY ([Id]),
    CONSTRAINT [FK_Processes_Models_ModelId] FOREIGN KEY ([ModelId]) REFERENCES [SCH_RD].[Models] ([Id]) ON DELETE NO ACTION
);
GO


CREATE TABLE [SCH_RD].[CharacteristicValues] (
    [ModelId] uniqueidentifier NOT NULL,
    [CharacteristicId] uniqueidentifier NOT NULL,
    [Value] nvarchar(500) NOT NULL,
    CONSTRAINT [PK_CharacteristicValues] PRIMARY KEY ([ModelId], [CharacteristicId]),
    CONSTRAINT [FK_CharacteristicValues_CharacteristicDefinitions_CharacteristicId] FOREIGN KEY ([CharacteristicId]) REFERENCES [SCH_RD].[CharacteristicDefinitions] ([Id]) ON DELETE NO ACTION,
    CONSTRAINT [FK_CharacteristicValues_Models_ModelId] FOREIGN KEY ([ModelId]) REFERENCES [SCH_RD].[Models] ([Id]) ON DELETE NO ACTION
);
GO


CREATE TABLE [SCH_RD].[Steps] (
    [Id] uniqueidentifier NOT NULL,
    [ProcessId] uniqueidentifier NOT NULL,
    [Position] int NOT NULL,
    [Name] nvarchar(120) NOT NULL,
    [Description] nvarchar(2000) NOT NULL,
    CONSTRAINT [PK_Steps] PRIMARY KEY ([Id]),
    CONSTRAINT [FK_Steps_Processes_ProcessId] FOREIGN KEY ([ProcessId]) REFERENCES [SCH_RD].[Processes] ([Id]) ON DELETE NO ACTION
);
GO


CREATE TABLE [SCH_RD].[TestDefinitions] (
    [Id] uniqueidentifier NOT NULL,
    [ProcessId] uniqueidentifier NOT NULL,
    [StepId] uniqueidentifier NULL,
    [Name] nvarchar(120) NOT NULL,
    [Description] nvarchar(2000) NOT NULL,
    [ExpectedResult] nvarchar(2000) NOT NULL,
    [Revision] int NOT NULL,
    CONSTRAINT [PK_TestDefinitions] PRIMARY KEY ([Id]),
    CONSTRAINT [FK_TestDefinitions_Processes_ProcessId] FOREIGN KEY ([ProcessId]) REFERENCES [SCH_RD].[Processes] ([Id]) ON DELETE NO ACTION,
    CONSTRAINT [FK_TestDefinitions_Steps_StepId] FOREIGN KEY ([StepId]) REFERENCES [SCH_RD].[Steps] ([Id]) ON DELETE NO ACTION
);
GO


CREATE TABLE [SCH_TEST].[TestExecutions] (
    [Id] uniqueidentifier NOT NULL,
    [TestId] uniqueidentifier NOT NULL,
    [ModelRevision] int NOT NULL,
    [Outcome] nvarchar(256) NOT NULL,
    [Observations] nvarchar(2000) NOT NULL,
    [ExecutedBy] nvarchar(256) NOT NULL,
    [ExecutedAt] datetimeoffset NOT NULL,
    [Validation] nvarchar(256) NOT NULL,
    [ValidatedBy] nvarchar(256) NULL,
    [ValidatedAt] datetimeoffset NULL,
    [ValidationComment] nvarchar(2000) NOT NULL,
    CONSTRAINT [PK_TestExecutions] PRIMARY KEY ([Id]),
    CONSTRAINT [FK_TestExecutions_TestDefinitions_TestId] FOREIGN KEY ([TestId]) REFERENCES [SCH_RD].[TestDefinitions] ([Id]) ON DELETE NO ACTION
);
GO


CREATE INDEX [IX_AuditEvents_At] ON [SCH_TEST].[AuditEvents] ([At]);
GO


CREATE INDEX [IX_CharacteristicDefinitions_ModelId] ON [SCH_RD].[CharacteristicDefinitions] ([ModelId]);
GO


CREATE UNIQUE INDEX [IX_CharacteristicDefinitions_Scope_NormalizedName] ON [SCH_RD].[CharacteristicDefinitions] ([Scope], [NormalizedName]);
GO


CREATE INDEX [IX_CharacteristicValues_CharacteristicId] ON [SCH_RD].[CharacteristicValues] ([CharacteristicId]);
GO


CREATE INDEX [IX_Compositions_IngredientId] ON [SCH_RD].[Compositions] ([IngredientId]);
GO


CREATE UNIQUE INDEX [IX_Ingredients_NormalizedName] ON [SCH_RD].[Ingredients] ([NormalizedName]);
GO


CREATE UNIQUE INDEX [IX_Models_NormalizedName] ON [SCH_RD].[Models] ([NormalizedName]);
GO


CREATE UNIQUE INDEX [IX_Processes_ModelId_NormalizedName] ON [SCH_RD].[Processes] ([ModelId], [NormalizedName]);
GO


CREATE INDEX [IX_Steps_ProcessId] ON [SCH_RD].[Steps] ([ProcessId]);
GO


CREATE INDEX [IX_TestDefinitions_ProcessId] ON [SCH_RD].[TestDefinitions] ([ProcessId]);
GO


CREATE INDEX [IX_TestDefinitions_StepId] ON [SCH_RD].[TestDefinitions] ([StepId]);
GO


CREATE INDEX [IX_TestExecutions_TestId] ON [SCH_TEST].[TestExecutions] ([TestId]);
GO



GO
IF SCHEMA_ID(N'SCH_PROD') IS NULL EXEC(N'CREATE SCHEMA [SCH_PROD] AUTHORIZATION [dbo]');
ALTER AUTHORIZATION ON SCHEMA::[SCH_RD] TO [dbo];
ALTER AUTHORIZATION ON SCHEMA::[SCH_TEST] TO [dbo];
GO
CREATE VIEW [SCH_PROD].[ValidatedModels] AS SELECT Id, Revision, Name, Description, UnitPriceCents, [Range] FROM [SCH_RD].[Models] WHERE State = 'Validated';
GO
CREATE VIEW [SCH_PROD].[ValidatedRecipes] AS SELECT c.ModelId, c.IngredientId, i.Name, c.QuantityCg FROM [SCH_RD].[Compositions] c JOIN [SCH_RD].[Ingredients] i ON i.Id=c.IngredientId JOIN [SCH_RD].[Models] m ON m.Id=c.ModelId WHERE m.State='Validated';
GO
CREATE VIEW [SCH_PROD].[ValidatedCharacteristics] AS SELECT v.ModelId, v.CharacteristicId, d.Name, d.Kind, d.Unit, v.Value FROM [SCH_RD].[CharacteristicValues] v JOIN [SCH_RD].[CharacteristicDefinitions] d ON d.Id=v.CharacteristicId JOIN [SCH_RD].[Models] m ON m.Id=v.ModelId WHERE m.State='Validated';
GO
CREATE VIEW [SCH_PROD].[ValidatedProcesses] AS SELECT p.Id, p.ModelId, p.Revision, p.Name, p.Description FROM [SCH_RD].[Processes] p JOIN [SCH_RD].[Models] m ON m.Id=p.ModelId WHERE m.State='Validated';
GO
CREATE VIEW [SCH_PROD].[ValidatedSteps] AS SELECT s.Id, s.ProcessId, s.Position, s.Name, s.Description FROM [SCH_RD].[Steps] s JOIN [SCH_RD].[Processes] p ON p.Id=s.ProcessId JOIN [SCH_RD].[Models] m ON m.Id=p.ModelId WHERE m.State='Validated';

