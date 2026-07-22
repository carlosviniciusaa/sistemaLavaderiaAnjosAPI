using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LavanderiaAnjos.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class ImportacaoInicialClientes : Migration
    {
        private static readonly string[] UsuarioAndCreatedColumns = ["UsuarioId", "CriadoEm"];

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ImportacoesClientes",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    HashArquivo = table.Column<string>(type: "char(64)", nullable: false),
                    NomeArquivo = table.Column<string>(type: "nvarchar(260)", maxLength: 260, nullable: false),
                    Status = table.Column<int>(type: "int", nullable: false),
                    CriadoEm = table.Column<DateTime>(type: "datetime2", nullable: false),
                    ExpiraEm = table.Column<DateTime>(type: "datetime2", nullable: false),
                    ConfirmadoEm = table.Column<DateTime>(type: "datetime2", nullable: true),
                    UsuarioId = table.Column<int>(type: "int", nullable: false),
                    QuantidadeTotal = table.Column<int>(type: "int", nullable: false),
                    QuantidadeValida = table.Column<int>(type: "int", nullable: false),
                    QuantidadeComAlerta = table.Column<int>(type: "int", nullable: false),
                    QuantidadeImportada = table.Column<int>(type: "int", nullable: false),
                    QuantidadeIgnorada = table.Column<int>(type: "int", nullable: false),
                    QuantidadeInvalida = table.Column<int>(type: "int", nullable: false),
                    QuantidadeDuplicada = table.Column<int>(type: "int", nullable: false),
                    QuantidadeExistente = table.Column<int>(type: "int", nullable: false),
                    QuantidadeSelecionada = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ImportacoesClientes", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ImportacoesClientes_Usuarios_UsuarioId",
                        column: x => x.UsuarioId,
                        principalTable: "Usuarios",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ImportacoesClientes_UsuarioId_CriadoEm",
                table: "ImportacoesClientes",
                columns: UsuarioAndCreatedColumns);

            migrationBuilder.CreateIndex(
                name: "UX_ImportacoesClientes_Hash_Concluida",
                table: "ImportacoesClientes",
                column: "HashArquivo",
                unique: true,
                filter: "[Status] = 2");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ImportacoesClientes");
        }
    }
}
