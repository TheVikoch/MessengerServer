using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MessengerServer.Migrations
{
    /// <inheritdoc />
    public partial class AddConversationAvatars : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "AvatarContentType",
                table: "Conversations",
                type: "character varying(128)",
                maxLength: 128,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "AvatarObjectKey",
                table: "Conversations",
                type: "character varying(512)",
                maxLength: 512,
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "AvatarPhotoId",
                table: "Conversations",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "AvatarSize",
                table: "Conversations",
                type: "bigint",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "AvatarUpdatedAt",
                table: "Conversations",
                type: "timestamp with time zone",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "AvatarContentType",
                table: "Conversations");

            migrationBuilder.DropColumn(
                name: "AvatarObjectKey",
                table: "Conversations");

            migrationBuilder.DropColumn(
                name: "AvatarPhotoId",
                table: "Conversations");

            migrationBuilder.DropColumn(
                name: "AvatarSize",
                table: "Conversations");

            migrationBuilder.DropColumn(
                name: "AvatarUpdatedAt",
                table: "Conversations");
        }
    }
}
