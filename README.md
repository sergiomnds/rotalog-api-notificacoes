# RotaLog API Notificações

Microsserviço de notificações — envio de emails e SMS transacionais.

## Stack

- .NET Core 6
- Entity Framework Core
- MediatR
- PostgreSQL (schema `notificacoes`)

## Como rodar

Pré-requisito: PostgreSQL rodando via `rotalog-workspace`.

```bash
dotnet restore
dotnet run
```

A API sobe na porta **5000**.

## Estrutura do projeto

O projeto segue uma estrutura inspirada em Clean Architecture:

```
├── Domain/            # Entidades e interfaces
├── Application/       # Handlers e lógica de aplicação
├── Infrastructure/    # Acesso a dados, serviços externos
├── Controllers/       # Endpoints REST
└── Program.cs
```

## Endpoints principais

| Método | Rota                        | Descrição                      |
|--------|-----------------------------|--------------------------------|
| POST   | /api/notificacoes/email     | Enviar notificação por email   |
| POST   | /api/notificacoes/sms       | Enviar notificação por SMS     |
| GET    | /api/notificacoes/historico | Histórico de envios            |
| POST   | /api/notificacoes/alertas-manutencao | Alerta de manutenção preventiva (chamado pela api-frotas) |
| GET    | /health                     | Health check do serviço        |

### Alerta de manutenção preventiva

`POST /api/notificacoes/alertas-manutencao` com `alertaId`, `veiculoId`, `placa`, `motivo`
(`QUILOMETRAGEM`, `TEMPO` ou `QUILOMETRAGEM_E_TEMPO`), `destinatario` e `mensagem`. Usa o template
`ALERTA_MANUTENCAO_PREVENTIVA` (script `12-seed-template-alerta-manutencao-preventiva.sql` do workspace).

| Resposta | Significado |
|----------|-------------|
| 201 | e-mail enviado |
| 200 | alerta já havia sido enviado antes (idempotente por `alertaId`, não reenvia) |
| 400 | pedido inválido |
| 503 | e-mail não enviado agora; a api-frotas mantém o alerta `PENDENTE` e tenta de novo |

Diferente de `POST /api/notificacoes`, só responde 2xx quando o e-mail foi de fato enviado.

## Testes

```
dotnet test tests/api-notificacoes.Tests
```
