-- REFERENCE ONLY: Ramzi must review and execute as an authorized database administrator.
-- Run in the KillerBee database after schema.sql. No login, password, server privilege or service-account delegation is created here.
IF DATABASE_PRINCIPAL_ID(N'kb_rd') IS NULL CREATE ROLE [kb_rd];
IF DATABASE_PRINCIPAL_ID(N'kb_test') IS NULL CREATE ROLE [kb_test];
IF DATABASE_PRINCIPAL_ID(N'kb_prod') IS NULL CREATE ROLE [kb_prod];
GO
GRANT SELECT, INSERT, UPDATE, DELETE ON SCHEMA::[SCH_RD] TO [kb_rd];
GRANT SELECT ON OBJECT::[SCH_TEST].[TestExecutions] TO [kb_rd];
GRANT SELECT, INSERT ON OBJECT::[SCH_TEST].[AuditEvents] TO [kb_rd];
GRANT SELECT ON SCHEMA::[SCH_PROD] TO [kb_rd];
GO
GRANT SELECT ON SCHEMA::[SCH_RD] TO [kb_test];
GRANT SELECT, INSERT, UPDATE ON OBJECT::[SCH_TEST].[TestExecutions] TO [kb_test];
GRANT UPDATE ON OBJECT::[SCH_RD].[Models] ([State]) TO [kb_test];
GRANT INSERT ON OBJECT::[SCH_TEST].[AuditEvents] TO [kb_test];
GRANT SELECT ON OBJECT::[SCH_TEST].[AuditEvents] ([Id]) TO [kb_test];
GRANT SELECT ON SCHEMA::[SCH_PROD] TO [kb_test];
GO
GRANT SELECT ON SCHEMA::[SCH_PROD] TO [kb_prod];
GO
-- Provision each real Windows login and mapped database user individually, then assign its business role.
-- Example names below are documentation, not commands to run unchanged:
-- CREATE LOGIN [KILLERBEE\prenom.nom] FROM WINDOWS;           -- server context: Ramzi
-- CREATE USER [KILLERBEE\prenom.nom] FOR LOGIN [KILLERBEE\prenom.nom] WITH DEFAULT_SCHEMA = [SCH_RD];
-- ALTER ROLE [kb_rd] ADD MEMBER [KILLERBEE\prenom.nom];
-- DBA/BCK/USR server profiles, SQL/Agent service accounts and backups remain in Ramzi's administration scope.
