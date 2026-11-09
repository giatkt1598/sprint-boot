using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SprintBoot.EntityFrameworkCore.Migrations
{
    /// <summary>
    /// The product calls an issue a ticket now, in code and data as well as on screen. Every schema, table, column, index
    /// and constraint named after issues is renamed in place, so no row moves, and the stored names that point at them
    /// (permission keys, notification event types, branch name placeholders) follow.
    /// </summary>
    public partial class RenameIssuesToTickets : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder) => migrationBuilder.Sql(Rename("issue", "ticket", "Issue", "Ticket"));

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder) => migrationBuilder.Sql(Rename("ticket", "issue", "Ticket", "Issue"));

        /// <summary>Renames database objects and stored names from one word to the other, in lower and title case.</summary>
        private static string Rename(string from, string to, string fromTitle, string toTitle) => $$"""
            DO $rename$
            DECLARE r record;
            BEGIN
              IF EXISTS (SELECT 1 FROM pg_namespace WHERE nspname = '{{from}}s') AND NOT EXISTS (SELECT 1 FROM pg_namespace WHERE nspname = '{{to}}s') THEN
                EXECUTE 'ALTER SCHEMA "{{from}}s" RENAME TO "{{to}}s"';
              END IF;

              -- Tables, then columns, then indexes and constraints, in every schema of the app.
              FOR r IN SELECT n.nspname s, c.relname t FROM pg_class c JOIN pg_namespace n ON n.oid = c.relnamespace
                       WHERE c.relkind IN ('r', 'p') AND n.nspname NOT IN ('pg_catalog', 'information_schema') AND c.relname LIKE '%{{from}}%'
              LOOP
                EXECUTE format('ALTER TABLE %I.%I RENAME TO %I', r.s, r.t, replace(r.t, '{{from}}', '{{to}}'));
              END LOOP;

              FOR r IN SELECT table_schema s, table_name t, column_name c FROM information_schema.columns
                       WHERE table_schema NOT IN ('pg_catalog', 'information_schema') AND (column_name LIKE '%{{fromTitle}}%' OR column_name LIKE '%{{from}}%')
              LOOP
                EXECUTE format('ALTER TABLE %I.%I RENAME COLUMN %I TO %I', r.s, r.t, r.c, replace(replace(r.c, '{{fromTitle}}', '{{toTitle}}'), '{{from}}', '{{to}}'));
              END LOOP;

              FOR r IN SELECT n.nspname s, c.relname i FROM pg_class c JOIN pg_namespace n ON n.oid = c.relnamespace
                       WHERE c.relkind = 'i' AND n.nspname NOT IN ('pg_catalog', 'information_schema') AND (c.relname LIKE '%{{fromTitle}}%' OR c.relname LIKE '%{{from}}%')
                         AND NOT EXISTS (SELECT 1 FROM pg_constraint k WHERE k.conindid = c.oid AND k.contype IN ('p', 'u'))
              LOOP
                EXECUTE format('ALTER INDEX %I.%I RENAME TO %I', r.s, r.i, replace(replace(r.i, '{{fromTitle}}', '{{toTitle}}'), '{{from}}', '{{to}}'));
              END LOOP;

              FOR r IN SELECT n.nspname s, t.relname t, k.conname c FROM pg_constraint k JOIN pg_class t ON t.oid = k.conrelid JOIN pg_namespace n ON n.oid = t.relnamespace
                       WHERE n.nspname NOT IN ('pg_catalog', 'information_schema') AND (k.conname LIKE '%{{fromTitle}}%' OR k.conname LIKE '%{{from}}%')
              LOOP
                EXECUTE format('ALTER TABLE %I.%I RENAME CONSTRAINT %I TO %I', r.s, r.t, r.c, replace(replace(r.c, '{{fromTitle}}', '{{toTitle}}'), '{{from}}', '{{to}}'));
              END LOOP;

              -- Stored names: space role permissions, ABP permissions, notification event types and branch name placeholders.
              UPDATE projects.project_role_permissions SET "PermissionKey" = replace("PermissionKey", '{{fromTitle}}.', '{{toTitle}}.') WHERE "PermissionKey" LIKE '{{fromTitle}}.%';
              UPDATE public."AbpPermissionGrants" SET "Name" = replace("Name", '{{fromTitle}}', '{{toTitle}}') WHERE "Name" LIKE 'SprintBoot.%{{fromTitle}}%';
              UPDATE public."AbpPermissions" SET "Name" = replace("Name", '{{fromTitle}}', '{{toTitle}}') WHERE "Name" LIKE 'SprintBoot.%{{fromTitle}}%';
              UPDATE notifications.notifications SET "EventType" = replace("EventType", '{{from}}.', '{{to}}.') WHERE "EventType" LIKE '{{from}}.%';
              UPDATE notifications.rules SET "EventType" = replace("EventType", '{{from}}.', '{{to}}.') WHERE "EventType" LIKE '{{from}}.%';
              UPDATE notifications.preferences SET "EventType" = replace("EventType", '{{from}}.', '{{to}}.') WHERE "EventType" LIKE '{{from}}.%';
              UPDATE projects.projects SET "BranchNameTemplate" = replace(replace("BranchNameTemplate", '{{{from}}_key}', '{{{to}}_key}'), '{{{from}}_title}', '{{{to}}_title}') WHERE "BranchNameTemplate" LIKE '%{{{from}}_%';
            END $rename$;
            """;
    }
}
