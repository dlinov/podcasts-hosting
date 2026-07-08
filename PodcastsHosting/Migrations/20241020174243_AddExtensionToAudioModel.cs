using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PodcastsHosting.Migrations
{
    /// <inheritdoc />
    public partial class AddExtensionToAudioModel : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_AudioModels_AspNetUsers_UploadUserId",
                table: "AudioModels");

            migrationBuilder.AlterColumn<string>(
                name: "UploadUserId",
                table: "AudioModels",
                type: "nvarchar(450)",
                nullable: true,
                oldClrType: typeof(string),
                oldType: "nvarchar(450)");

            migrationBuilder.AddColumn<string>(
                name: "Extension",
                table: "AudioModels",
                type: "nvarchar(15)",
                maxLength: 15,
                nullable: true);

            migrationBuilder.AddForeignKey(
                name: "FK_AudioModels_AspNetUsers_UploadUserId",
                table: "AudioModels",
                column: "UploadUserId",
                principalTable: "AspNetUsers",
                principalColumn: "Id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_AudioModels_AspNetUsers_UploadUserId",
                table: "AudioModels");

            migrationBuilder.DropColumn(
                name: "Extension",
                table: "AudioModels");

            migrationBuilder.AlterColumn<string>(
                name: "UploadUserId",
                table: "AudioModels",
                type: "nvarchar(450)",
                nullable: false,
                defaultValue: "",
                oldClrType: typeof(string),
                oldType: "nvarchar(450)",
                oldNullable: true);

            migrationBuilder.AddForeignKey(
                name: "FK_AudioModels_AspNetUsers_UploadUserId",
                table: "AudioModels",
                column: "UploadUserId",
                principalTable: "AspNetUsers",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);
        }
    }
}
