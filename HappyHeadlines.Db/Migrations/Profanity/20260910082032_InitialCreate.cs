using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace HappyHeadlines.Db.Migrations.Profanity
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ProfanityWords",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Word = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ProfanityWords", x => x.Id);
                });

            migrationBuilder.InsertData(
                table: "ProfanityWords",
                columns: new[] { "Id", "Word" },
                values: new object[,]
                {
                    { 1, "damn" },
                    { 2, "hell" },
                    { 3, "crap" },
                    { 4, "bloody" },
                    { 5, "arse" },
                    { 6, "bugger" },
                    { 7, "shit" },
                    { 8, "piss" },
                    { 9, "dick" },
                    { 10, "bastard" },
                    { 11, "asshole" },
                    { 12, "bollocks" }
                });

            migrationBuilder.CreateIndex(
                name: "IX_ProfanityWords_Word",
                table: "ProfanityWords",
                column: "Word",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ProfanityWords");
        }
    }
}
