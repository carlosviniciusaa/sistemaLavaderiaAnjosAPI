using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LavanderiaAnjos.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class GerenciamentoAdministradores : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "LoginNormalizado",
                table: "Usuarios",
                type: "nvarchar(100)",
                maxLength: 100,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<long>(
                name: "Versao",
                table: "Usuarios",
                type: "bigint",
                nullable: false,
                defaultValue: 1L);

            migrationBuilder.AddColumn<int>(
                name: "VersaoSessao",
                table: "Usuarios",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.Sql("""
                UPDATE [Usuarios]
                SET [LoginNormalizado] = LOWER(LTRIM(RTRIM([Login])));

                IF EXISTS (
                    SELECT [LoginNormalizado]
                    FROM [Usuarios]
                    GROUP BY [LoginNormalizado]
                    HAVING COUNT_BIG(*) > 1
                )
                    THROW 51000, 'Existem logins duplicados após a normalização. A migration foi interrompida sem alterar os dados.', 1;
                """);

            migrationBuilder.CreateIndex(
                name: "UX_Usuarios_LoginNormalizado",
                table: "Usuarios",
                column: "LoginNormalizado",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "UX_Usuarios_LoginNormalizado",
                table: "Usuarios");

            migrationBuilder.DropColumn(
                name: "LoginNormalizado",
                table: "Usuarios");

            migrationBuilder.DropColumn(
                name: "Versao",
                table: "Usuarios");

            migrationBuilder.DropColumn(
                name: "VersaoSessao",
                table: "Usuarios");
        }
    }
}
