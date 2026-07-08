using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PodcastsHosting.Migrations
{
    /// <inheritdoc />
    public partial class AddAudioModel : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "AudioModels",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    FileName = table.Column<string>(type: "nvarchar(255)", maxLength: 255, nullable: false),
                    FilePath = table.Column<string>(type: "nvarchar(255)", maxLength: 255, nullable: false),
                    FileHash = table.Column<string>(type: "nvarchar(255)", maxLength: 255, nullable: false),
                    UploadTime = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UploadUserId = table.Column<string>(type: "nvarchar(450)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AudioModels", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AudioModels_AspNetUsers_UploadUserId",
                        column: x => x.UploadUserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_AudioModels_UploadUserId",
                table: "AudioModels",
                column: "UploadUserId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AudioModels");
        }
    }
}
