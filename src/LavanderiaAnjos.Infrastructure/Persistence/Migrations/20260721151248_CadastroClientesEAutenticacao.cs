using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LavanderiaAnjos.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class CadastroClientesEAutenticacao : Migration
    {
        private static readonly string[] ActiveNameColumns = ["Ativo", "NomeRazaoSocial"];

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Clientes",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    CodigoLegado = table.Column<int>(type: "int", nullable: true),
                    NomeRazaoSocial = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    Documento = table.Column<string>(type: "varchar(14)", nullable: false),
                    TipoPessoa = table.Column<string>(type: "varchar(20)", nullable: false),
                    ProdutorRural = table.Column<bool>(type: "bit", nullable: false),
                    InscricaoEstadual = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: true),
                    InscricaoMunicipal = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: true),
                    DataInicioRelacionamento = table.Column<DateOnly>(type: "date", nullable: true),
                    Cep = table.Column<string>(type: "varchar(8)", nullable: true),
                    Logradouro = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    Numero = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: true),
                    Complemento = table.Column<string>(type: "nvarchar(150)", maxLength: 150, nullable: true),
                    Bairro = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    Cidade = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    Uf = table.Column<string>(type: "varchar(2)", nullable: true),
                    Telefone = table.Column<string>(type: "varchar(20)", nullable: true),
                    EmailEnvio = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    EmailNotaFiscal = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    Observacoes = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: true),
                    Ativo = table.Column<bool>(type: "bit", nullable: false, defaultValue: true),
                    CriadoEm = table.Column<DateTime>(type: "datetime2", nullable: false),
                    AtualizadoEm = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Clientes", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Usuarios",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Nome = table.Column<string>(type: "nvarchar(150)", maxLength: 150, nullable: false),
                    Login = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    SenhaHash = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: false),
                    Ativo = table.Column<bool>(type: "bit", nullable: false, defaultValue: true),
                    CriadoEm = table.Column<DateTime>(type: "datetime2", nullable: false),
                    AtualizadoEm = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Usuarios", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Clientes_Ativo_NomeRazaoSocial",
                table: "Clientes",
                columns: ActiveNameColumns);

            migrationBuilder.CreateIndex(
                name: "IX_Clientes_CodigoLegado",
                table: "Clientes",
                column: "CodigoLegado",
                filter: "[CodigoLegado] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_Clientes_NomeRazaoSocial",
                table: "Clientes",
                column: "NomeRazaoSocial");

            migrationBuilder.CreateIndex(
                name: "UX_Clientes_Documento_Ativo",
                table: "Clientes",
                column: "Documento",
                unique: true,
                filter: "[Ativo] = 1");

            migrationBuilder.CreateIndex(
                name: "UX_Usuarios_Login",
                table: "Usuarios",
                column: "Login",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "Clientes");

            migrationBuilder.DropTable(
                name: "Usuarios");
        }
    }
}
