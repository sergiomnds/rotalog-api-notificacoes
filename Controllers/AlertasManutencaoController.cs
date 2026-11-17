using Microsoft.AspNetCore.Mvc;
using api_notificacoes.DTOs;
using api_notificacoes.Services;

namespace api_notificacoes.Controllers;

/// <summary>
/// Recebe pedidos de alerta de manutenção preventiva do api-frotas e envia o e-mail ao gestor.
/// </summary>
[ApiController]
[Route("api/notificacoes/alertas-manutencao")]
public class AlertasManutencaoController : ControllerBase
{
    private readonly AlertaManutencaoService _service;
    private readonly ILogger<AlertasManutencaoController> _logger;

    public AlertasManutencaoController(AlertaManutencaoService service, ILogger<AlertasManutencaoController> logger)
    {
        _service = service;
        _logger = logger;
    }

    /// <summary>
    /// POST /api/notificacoes/alertas-manutencao
    ///
    /// 201: e-mail enviado. 200: alerta já havia sido enviado antes (idempotente).
    /// 400: pedido inválido (o api-frotas registra FALHA). 503: e-mail não enviado agora; o api-frotas mantém o
    /// alerta PENDENTE e tenta de novo depois. O sucesso (2xx) só é devolvido quando o e-mail foi realmente enviado.
    /// </summary>
    [HttpPost]
    public async Task<IActionResult> Criar([FromBody] AlertaManutencaoRequest request)
    {
        try
        {
            var resultado = await _service.Processar(request);

            if (!resultado.Enviada)
            {
                return StatusCode(StatusCodes.Status503ServiceUnavailable, new AlertaNaoEnviadoResponse
                {
                    Error = "Não foi possível enviar o e-mail do alerta neste momento",
                    NotificacaoId = resultado.Notificacao.Id,
                    Status = resultado.Notificacao.Status
                });
            }

            return resultado.JaEnviadaAnteriormente
                ? Ok(resultado.Notificacao)
                : StatusCode(StatusCodes.Status201Created, resultado.Notificacao);
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new { error = ex.Message });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Erro ao processar alerta de manutenção {AlertaId}", request.AlertaId);
            return StatusCode(StatusCodes.Status500InternalServerError,
                new { error = "Erro ao processar alerta de manutenção" });
        }
    }
}
