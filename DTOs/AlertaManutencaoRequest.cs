using System.ComponentModel.DataAnnotations;

namespace api_notificacoes.DTOs;

/// <summary>
/// Pedido de alerta de manutenção preventiva enviado pelo api-frotas.
/// </summary>
public class AlertaManutencaoRequest
{
    /// <summary>ID do alerta no api-frotas. Garante idempotência: o mesmo alerta nunca gera dois e-mails.</summary>
    [Required]
    public long? AlertaId { get; set; }

    public long? VeiculoId { get; set; }

    [Required]
    [MaxLength(7)]
    public string Placa { get; set; } = string.Empty;

    /// <summary>QUILOMETRAGEM, TEMPO ou QUILOMETRAGEM_E_TEMPO.</summary>
    [Required]
    public string Motivo { get; set; } = string.Empty;

    [Required]
    [EmailAddress]
    [MaxLength(255)]
    public string Destinatario { get; set; } = string.Empty;

    [Required]
    public string Mensagem { get; set; } = string.Empty;
}

/// <summary>
/// Resposta de erro quando o e-mail do alerta não pôde ser enviado (o chamador deve tentar de novo depois).
/// </summary>
public class AlertaNaoEnviadoResponse
{
    public string Error { get; set; } = string.Empty;
    public long NotificacaoId { get; set; }
    public string Status { get; set; } = string.Empty;
}
