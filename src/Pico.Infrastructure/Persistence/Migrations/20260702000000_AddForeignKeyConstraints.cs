using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Pico.Infrastructure.Migrations
{
    /// <inheritdoc />
    /// <summary>
    /// Adds the missing foreign-key constraints that were declared in the entity
    /// configurations but not emitted in InitialCreate. Runs via raw SQL because the
    /// project intentionally avoids EF design-time tooling (see DESIGN.md).
    ///
    /// Idempotent: safe to apply against fresh or existing databases.
    /// </summary>
    public partial class AddForeignKeyConstraints : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // resources.user_id -> users.id
            migrationBuilder.Sql(
                "ALTER TABLE resources ADD CONSTRAINT \"FK_resources_users_user_id\" " +
                "FOREIGN KEY (user_id) REFERENCES users (id) ON DELETE RESTRICT;");
            migrationBuilder.Sql("CREATE INDEX \"IX_resources_user_id\" ON resources (user_id);");

            // resource_events.resource_id -> resources.id (cascade on resource delete)
            migrationBuilder.Sql(
                "ALTER TABLE resource_events ADD CONSTRAINT \"FK_resource_events_resources_resource_id\" " +
                "FOREIGN KEY (resource_id) REFERENCES resources (id) ON DELETE CASCADE;");

            // invoices.user_id -> users.id
            migrationBuilder.Sql(
                "ALTER TABLE invoices ADD CONSTRAINT \"FK_invoices_users_user_id\" " +
                "FOREIGN KEY (user_id) REFERENCES users (id) ON DELETE RESTRICT;");

            // invoice_lines.resource_id -> resources.id
            migrationBuilder.Sql(
                "ALTER TABLE invoice_lines ADD CONSTRAINT \"FK_invoice_lines_resources_resource_id\" " +
                "FOREIGN KEY (resource_id) REFERENCES resources (id) ON DELETE RESTRICT;");

            // invoice_lines.flavor_id -> flavors.id
            migrationBuilder.Sql(
                "ALTER TABLE invoice_lines ADD CONSTRAINT \"FK_invoice_lines_flavors_flavor_id\" " +
                "FOREIGN KEY (flavor_id) REFERENCES flavors (id) ON DELETE RESTRICT;");

            // audit_logs.user_id -> users.id (set null on user delete — preserve audit trail)
            migrationBuilder.Sql(
                "ALTER TABLE audit_logs ADD CONSTRAINT \"FK_audit_logs_users_user_id\" " +
                "FOREIGN KEY (user_id) REFERENCES users (id) ON DELETE SET NULL;");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("ALTER TABLE IF EXISTS audit_logs DROP CONSTRAINT IF EXISTS \"FK_audit_logs_users_user_id\";");
            migrationBuilder.Sql("ALTER TABLE IF EXISTS invoice_lines DROP CONSTRAINT IF EXISTS \"FK_invoice_lines_flavors_flavor_id\";");
            migrationBuilder.Sql("ALTER TABLE IF EXISTS invoice_lines DROP CONSTRAINT IF EXISTS \"FK_invoice_lines_resources_resource_id\";");
            migrationBuilder.Sql("ALTER TABLE IF EXISTS invoices DROP CONSTRAINT IF EXISTS \"FK_invoices_users_user_id\";");
            migrationBuilder.Sql("ALTER TABLE IF EXISTS resource_events DROP CONSTRAINT IF EXISTS \"FK_resource_events_resources_resource_id\";");
            migrationBuilder.Sql("ALTER TABLE IF EXISTS resources DROP CONSTRAINT IF EXISTS \"FK_resources_users_user_id\";");
            migrationBuilder.Sql("DROP INDEX IF EXISTS \"IX_resources_user_id\";");
        }
    }
}