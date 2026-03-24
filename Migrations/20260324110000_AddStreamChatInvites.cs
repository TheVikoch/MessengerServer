using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MessengerServer.Migrations
{
    /// <inheritdoc />
    public partial class AddStreamChatInvites : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "StreamChatInvites",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatorId = table.Column<Guid>(type: "uuid", nullable: false),
                    TargetUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    PersonalChatId = table.Column<Guid>(type: "uuid", nullable: false),
                    StreamChatId = table.Column<Guid>(type: "uuid", nullable: true),
                    StreamChatName = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    Token = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    Status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    ExpiresAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    AcceptedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    RevokedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_StreamChatInvites", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_StreamChatInvites_PersonalChatId_Status",
                table: "StreamChatInvites",
                columns: new[] { "PersonalChatId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_StreamChatInvites_Token",
                table: "StreamChatInvites",
                column: "Token",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "StreamChatInvites");
        }
    }
}
