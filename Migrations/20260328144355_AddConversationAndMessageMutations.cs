using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MessengerServer.Migrations
{
    /// <inheritdoc />
    public partial class AddConversationAndMessageMutations : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "ClearedAt",
                table: "ConversationMembers",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "DeletedMessagesForUsers",
                columns: table => new
                {
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    MessageId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    ConversationId = table.Column<Guid>(type: "uuid", nullable: false),
                    DeletedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DeletedMessagesForUsers", x => new { x.UserId, x.MessageId });
                    table.ForeignKey(
                        name: "FK_DeletedMessagesForUsers_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_DeletedMessagesForUsers_UserId_ConversationId",
                table: "DeletedMessagesForUsers",
                columns: new[] { "UserId", "ConversationId" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "DeletedMessagesForUsers");

            migrationBuilder.DropColumn(
                name: "ClearedAt",
                table: "ConversationMembers");
        }
    }
}
