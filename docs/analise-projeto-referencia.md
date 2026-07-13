# Análise do projeto de referência

Foram encontrados dois projetos irmãos, analisados somente para referência e sem alterações.

## SistemaClientesAPI

- .NET 10 (`net10.0`), ASP.NET Core Minimal API e Entity Framework Core.
- Frontend estático na mesma aplicação, com HTML, CSS e JavaScript sem framework.
- Identidade visual clara, responsiva, com verde petróleo, cartões, tabelas e modos claro/escuro.
- Autenticação por cookie, senhas com `PasswordHasher` e autorização por níveis 1 e 2.
- Entidades e `DbContext` em pastas próprias, porém endpoints, validações, DTOs e rotinas de banco concentrados em `Program.cs`.
- Logs de auditoria em tabela e logging técnico no console.
- Mensagens de sucesso/erro consumidas como JSON e apresentadas no frontend.
- Navegação por rotas simples (`/login`, `/clientes`, `/administradores`, `/logs`).
- Nomes em português e classes/propriedades em PascalCase.

Riscos que não foram copiados: segredo versionado em configuração, `EnsureCreated`, DDL executado na inicialização e criação automática de usuário padrão.

## sistemaDeCadastroDeClientes

- .NET 8 (`net8.0`).
- Aplicação de console, dados somente em memória e sem frontend, banco, autenticação ou logs.
- Útil apenas como referência histórica de idioma e nomenclatura.

## Decisão aplicada

Foi mantida a simplicidade operacional do projeto web principal, o frontend integrado, a navegação direta e o padrão visual. A nova solução usa camadas pequenas para evitar a concentração de responsabilidades e permite uma única publicação web no servidor.
