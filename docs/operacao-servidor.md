# Operação do servidor

## Serviço

```powershell
Get-Service LavanderiaAnjos
Get-CimInstance Win32_Service -Filter "Name='LavanderiaAnjos'" |
    Select-Object Name, State, StartMode, StartName, PathName
```

Resultado esperado: `Running`, início `Auto` e conta `NT AUTHORITY\LocalService`.

```powershell
D:\LavanderiaAnjos\Sistema\scripts\Start-Service.ps1
D:\LavanderiaAnjos\Sistema\scripts\Stop-Service.ps1
```

Após reinicializar o Windows, aguarde a rede e execute:

```powershell
Invoke-RestMethod http://127.0.0.1:5080/health
D:\LavanderiaAnjos\Sistema\scripts\Test-Production.ps1 -ServerIp <IP-confirmado>
```

## Saúde e porta

```powershell
Get-NetTCPConnection -LocalPort 5080 -State Listen
Invoke-RestMethod http://127.0.0.1:5080/health
Invoke-RestMethod http://SISTEMALAV:5080/health
```

O retorno esperado é `status=Healthy` e `database=Healthy`. `/health` não divulga servidor, banco ou credenciais.

## Logs

Os logs ficam em `D:\LavanderiaAnjos\Sistema\logs\lavanderia-AAAAMMDD.log`, são rotacionados diariamente e mantidos por 30 dias. Senhas, connection strings, e-mails e CPF/CNPJ são sanitizados. Exceções inesperadas incluem o código de correlação devolvido ao navegador.

```powershell
Get-Content D:\LavanderiaAnjos\Sistema\logs\lavanderia-$(Get-Date -Format yyyyMMdd).log -Tail 200
```

Falha de escrita de log não derruba a aplicação. Corrija somente a ACL de `logs` para o SID `S-1-5-19` (`LOCAL SERVICE`); não conceda acesso amplo ao disco.

## Configuração externa

Verifique apenas a existência, sem imprimir o valor:

```powershell
$configured = -not [string]::IsNullOrWhiteSpace(
    [Environment]::GetEnvironmentVariable(
        'ConnectionStrings__LavanderiaAnjos',
        [EnvironmentVariableTarget]::Machine))
$configured
```

Para alterar a conexão, pare o serviço, grave o novo valor pelo procedimento seguro de `implantacao-producao.md`, atualize a entrada `Environment` do serviço por procedimento administrativo protegido, reinicie e valide `/health`. Nunca registre o valor em arquivo.

## Falha após reinício

1. confira o serviço e a porta 5080;
2. consulte o log pelo código de correlação;
3. confirme que SQL Server `LAVANDERIA` está iniciado e a porta 51433 não mudou;
4. valide se a configuração externa existe sem imprimi-la;
5. use `Rollback-Service.ps1` se a falha começou após atualização.

Não execute migrations, restaure backup ou altere clientes para recuperar apenas o serviço web.

## Firewall

```powershell
Get-NetFirewallRule -DisplayName 'Lavanderia Anjos Web TCP 5080' |
    Get-NetFirewallPortFilter
Get-NetFirewallRule -DisplayName 'Lavanderia Anjos Web TCP 5080' |
    Get-NetFirewallAddressFilter
```

A regra deve aceitar somente TCP 5080, perfil privado e origem `192.168.68.0/24`. Não crie regra para SQL Server.

## Backup

A rotina diária existente continua sendo a fonte oficial. Após implantação bem-sucedida, acione-a uma vez, confirme `WITH CHECKSUM` e execute `RESTORE VERIFYONLY`. Não remova backups anteriores.
