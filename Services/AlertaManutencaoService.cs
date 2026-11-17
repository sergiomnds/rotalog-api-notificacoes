using api_notificacoes.Data;
using api_notificacoes.DTOs;
using Microsoft.EntityFrameworkCore;

namespace api_notificacoes.Services;

/// <summary>
/// Resultado do processamento de um alerta de manutenção preventiva.
/// </summary>
public record ResultadoAlertaManutencao(NotificacaoResponse Notificacao, bool Enviada, bool JaEnviadaAnteriormente);

/// <summary>
/// Envia por e-mail ao gestor o alerta de manutenção preventiva recebido do api-frotas.
/// Idempotente por alerta: repetir o pedido de um alerta já enviado não envia outro e-mail, e repetir o de um
/// alerta que falhou reaproveita a mesma notificação (reenvio) em vez de criar duplicatas.
/// </summary>
public class AlertaManutencaoService
{
    public const string Tipo = "ALERTA_MANUTENCAO_PREVENTIVA";
    public const string ServicoOrigem = "api-frotas";

    private static readonly Dictionary<string, string> MotivosLegiveis = new()
    {
        ["QUILOMETRAGEM"] = "Quilometragem acima do limite desde a última manutenção",
        ["TEMPO"] = "Tempo acima do limite desde a última manutenção",
        ["QUILOMETRAGEM_E_TEMPO"] = "Quilometragem e tempo acima dos limites desde a última manutenção"
    };

    private readonly NotificacoesDbContext _context;
    private readonly NotificacaoService _notificacaoService;
    private readonly ILogger<AlertaManutencaoService> _logger;

    public AlertaManutencaoService(
        NotificacoesDbContext context,
        NotificacaoService notificacaoService,
        ILogger<AlertaManutencaoService> logger)
    {
        _context = context;
        _notificacaoService = notificacaoService;
        _logger = logger;
    }

    /// <exception cref="ArgumentException">Motivo desconhecido.</exception>
    public virtual async Task<ResultadoAlertaManutencao> Processar(AlertaManutencaoRequest request)
    {
        if (!MotivosLegiveis.TryGetValue(request.Motivo, out var motivoLegivel))
            throw new ArgumentException($"Motivo inválido: {request.Motivo}");

        var referencia = $"alerta-manutencao-{request.AlertaId}";

        var existente = await _context.Notificacoes.FirstOrDefaultAsync(n =>
            n.Tipo == Tipo && n.ServicoOrigem == ServicoOrigem && n.ReferenciaId == referencia);

        NotificacaoResponse notificacao;
        if (existente == null)
        {
            notificacao = await _notificacaoService.CriarNotificacao(new NotificacaoRequest
            {
                Tipo = Tipo,
                Canal = "email",
                Destinatario = request.Destinatario,
                Assunto = $"ALERTA: Manutenção preventiva - Veículo {request.Placa}",
                Mensagem = request.Mensagem,
                ServicoOrigem = ServicoOrigem,
                ReferenciaId = referencia,
                Variaveis = new Dictionary<string, string>
                {
                    ["placa"] = request.Placa,
                    ["motivo"] = motivoLegivel,
                    ["mensagem"] = request.Mensagem
                }
            });
        }
        else if (existente.Status == "ENVIADO")
        {
            _logger.LogInformation("Alerta {AlertaId} já enviado (notificação {Id}); nada a fazer",
                request.AlertaId, existente.Id);
            var anterior = await _notificacaoService.BuscarPorId(existente.Id);
            return new ResultadoAlertaManutencao(anterior!, true, true);
        }
        else
        {
            _logger.LogInformation("Reenviando alerta {AlertaId} (notificação {Id}, status {Status})",
                request.AlertaId, existente.Id, existente.Status);
            notificacao = (await _notificacaoService.ReenviarNotificacao(
                existente.Id, new ReenvioRequest { NovoDestinatario = request.Destinatario }))!;
        }

        var enviada = notificacao.Status == "ENVIADO";
        if (!enviada)
        {
            _logger.LogWarning("Alerta {AlertaId} não foi enviado: notificação {Id} com status {Status} ({Erro})",
                request.AlertaId, notificacao.Id, notificacao.Status, notificacao.ErroMensagem);
        }

        return new ResultadoAlertaManutencao(notificacao, enviada, false);
    }
}
