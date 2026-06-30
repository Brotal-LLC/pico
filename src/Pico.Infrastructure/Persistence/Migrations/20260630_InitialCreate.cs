using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Pico.Infrastructure.Persistence.Migrations
{
    /// <summary>
    /// Initial schema for PICO — all 8 tables.
    /// Auto-generated equivalent of what `dotnet ef migrations add InitialCreate` produces.
    /// Applied at startup when Pico:Database:AutoMigrate=true.
    /// </summary>
    public partial class InitialCreate : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "users",
                columns: table => new
                {
                    id = table.Column<Guid>(nullable: false),
                    email = table.Column<string>(maxLength: 255, nullable: false),
                    name = table.Column<string>(maxLength: 100, nullable: false),
                    password_hash = table.Column<string>(maxLength: 255, nullable: false),
                    role = table.Column<string>(maxLength: 20, nullable: false),
                    created_at = table.Column<DateTimeOffset>(nullable: false)
                },
                constraints: table => table.PrimaryKey("PK_users", x => x.id));

            migrationBuilder.CreateIndex(
                name: "IX_users_email",
                table: "users",
                column: "email",
                unique: true);

            migrationBuilder.CreateTable(
                name: "flavors",
                columns: table => new
                {
                    id = table.Column<Guid>(nullable: false),
                    name = table.Column<string>(maxLength: 100, nullable: false),
                    vcpus = table.Column<int>(nullable: false),
                    ram_mb = table.Column<int>(nullable: false),
                    disk_gb = table.Column<int>(nullable: false),
                    price_per_hour = table.Column<decimal>(type: "numeric(12,4)", nullable: false),
                    price_per_month = table.Column<decimal>(type: "numeric(12,2)", nullable: false),
                    category = table.Column<string>(maxLength: 50, nullable: false),
                    active = table.Column<bool>(nullable: false)
                },
                constraints: table => table.PrimaryKey("PK_flavors", x => x.id));

            migrationBuilder.CreateIndex(
                name: "IX_flavors_name",
                table: "flavors",
                column: "name",
                unique: true);

            migrationBuilder.CreateTable(
                name: "images",
                columns: table => new
                {
                    id = table.Column<Guid>(nullable: false),
                    name = table.Column<string>(maxLength: 100, nullable: false),
                    os = table.Column<string>(maxLength: 50, nullable: false),
                    version = table.Column<string>(maxLength: 50, nullable: false),
                    size_gb = table.Column<int>(nullable: false),
                    active = table.Column<bool>(nullable: false)
                },
                constraints: table => table.PrimaryKey("PK_images", x => x.id));

            migrationBuilder.CreateIndex(
                name: "IX_images_name",
                table: "images",
                column: "name",
                unique: true);

            migrationBuilder.CreateTable(
                name: "resources",
                columns: table => new
                {
                    id = table.Column<Guid>(nullable: false),
                    user_id = table.Column<Guid>(nullable: false),
                    flavor_id = table.Column<Guid>(nullable: false),
                    image_id = table.Column<Guid>(nullable: false),
                    name = table.Column<string>(maxLength: 100, nullable: false),
                    status = table.Column<string>(maxLength: 20, nullable: false),
                    external_id = table.Column<string>(maxLength: 255, nullable: true),
                    ip_address = table.Column<string>(maxLength: 45, nullable: true),
                    created_at = table.Column<DateTimeOffset>(nullable: false),
                    updated_at = table.Column<DateTimeOffset>(nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_resources", x => x.id);
                    table.ForeignKey(
                        name: "FK_resources_flavors_flavor_id",
                        column: x => x.flavor_id,
                        principalTable: "flavors",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_resources_images_image_id",
                        column: x => x.image_id,
                        principalTable: "images",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_resources_user_id",
                table: "resources",
                column: "user_id");

            migrationBuilder.CreateIndex(
                name: "IX_resources_external_id",
                table: "resources",
                column: "external_id");

            migrationBuilder.CreateTable(
                name: "resource_events",
                columns: table => new
                {
                    id = table.Column<Guid>(nullable: false),
                    resource_id = table.Column<Guid>(nullable: false),
                    event_type = table.Column<string>(maxLength: 50, nullable: false),
                    old_status = table.Column<string>(maxLength: 20, nullable: false),
                    new_status = table.Column<string>(maxLength: 20, nullable: false),
                    message = table.Column<string>(maxLength: 500, nullable: false),
                    timestamp = table.Column<DateTimeOffset>(nullable: false)
                },
                constraints: table => table.PrimaryKey("PK_resource_events", x => x.id));

            migrationBuilder.CreateIndex(
                name: "IX_resource_events_resource_id_timestamp",
                table: "resource_events",
                columns: new[] { "resource_id", "timestamp" });

            migrationBuilder.CreateTable(
                name: "invoices",
                columns: table => new
                {
                    id = table.Column<Guid>(nullable: false),
                    user_id = table.Column<Guid>(nullable: false),
                    period_start = table.Column<DateTimeOffset>(nullable: false),
                    period_end = table.Column<DateTimeOffset>(nullable: false),
                    total = table.Column<decimal>(type: "numeric(12,2)", nullable: false),
                    status = table.Column<string>(maxLength: 20, nullable: false),
                    created_at = table.Column<DateTimeOffset>(nullable: false),
                    paid_at = table.Column<DateTimeOffset>(nullable: true)
                },
                constraints: table => table.PrimaryKey("PK_invoices", x => x.id));

            migrationBuilder.CreateIndex(
                name: "IX_invoices_user_id",
                table: "invoices",
                column: "user_id");

            migrationBuilder.CreateTable(
                name: "invoice_lines",
                columns: table => new
                {
                    id = table.Column<Guid>(nullable: false),
                    invoice_id = table.Column<Guid>(nullable: false),
                    resource_id = table.Column<Guid>(nullable: false),
                    flavor_id = table.Column<Guid>(nullable: false),
                    hours = table.Column<decimal>(type: "numeric(12,4)", nullable: false),
                    rate = table.Column<decimal>(type: "numeric(12,4)", nullable: false),
                    amount = table.Column<decimal>(type: "numeric(12,2)", nullable: false),
                    description = table.Column<string>(maxLength: 255, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_invoice_lines", x => x.id);
                    table.ForeignKey(
                        name: "FK_invoice_lines_invoices_invoice_id",
                        column: x => x.invoice_id,
                        principalTable: "invoices",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_invoice_lines_invoice_id",
                table: "invoice_lines",
                column: "invoice_id");

            migrationBuilder.CreateTable(
                name: "audit_logs",
                columns: table => new
                {
                    id = table.Column<Guid>(nullable: false),
                    user_id = table.Column<Guid>(nullable: true),
                    action = table.Column<string>(maxLength: 100, nullable: false),
                    entity_type = table.Column<string>(maxLength: 50, nullable: false),
                    entity_id = table.Column<Guid>(nullable: true),
                    details_json = table.Column<string>(type: "jsonb", nullable: false),
                    timestamp = table.Column<DateTimeOffset>(nullable: false)
                },
                constraints: table => table.PrimaryKey("PK_audit_logs", x => x.id));

            migrationBuilder.CreateIndex(
                name: "IX_audit_logs_timestamp",
                table: "audit_logs",
                column: "timestamp");
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable("audit_logs");
            migrationBuilder.DropTable("invoice_lines");
            migrationBuilder.DropTable("invoices");
            migrationBuilder.DropTable("resource_events");
            migrationBuilder.DropTable("resources");
            migrationBuilder.DropTable("images");
            migrationBuilder.DropTable("flavors");
            migrationBuilder.DropTable("users");
        }
    }
}
