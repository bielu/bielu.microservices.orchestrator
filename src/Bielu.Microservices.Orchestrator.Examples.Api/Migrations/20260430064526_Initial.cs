using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Bielu.Microservices.Orchestrator.Examples.Api.Migrations
{
    /// <inheritdoc />
    public partial class Initial : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ManagedInstances",
                columns: table => new
                {
                    Id = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    OrchestratorId = table.Column<Guid>(type: "uuid", nullable: false),
                    ContainerIds = table.Column<string>(type: "text", nullable: false),
                    OriginalRequest = table.Column<string>(type: "text", nullable: false),
                    DesiredState = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    DesiredReplicas = table.Column<int>(type: "integer", nullable: false),
                    ProviderName = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    Metadata = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ManagedInstances", x => x.Id);
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ManagedInstances");
        }
    }
}
