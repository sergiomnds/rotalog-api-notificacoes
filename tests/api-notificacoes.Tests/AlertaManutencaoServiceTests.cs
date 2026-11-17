using api_notificacoes.Data;
using api_notificacoes.DTOs;
using api_notificacoes.Models;
using api_notificacoes.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace api_notificacoes.Tests;

public class AlertaManutencaoServiceTests
{
    private static NotificacoesDbContext NovoContexto()
    {
        var options = new DbContextOptionsBuilder<NotificacoesDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        return new NotificacoesDbContext(options);
    }

    private static Mock<NotificacaoService> NovoNotificacaoServiceMock(NotificacoesDbContext context) =>
        new(context, NullLogger<NotificacaoService>.Instance, new ConfigurationBuilder().Build());

    private static AlertaManutencaoRequest Pedido(long alertaId = 7, string motivo = "QUILOMETRAGEM") => new()
    {
        AlertaId = alertaId,
        VeiculoId = 3,
        Placa = "ABC1D23",
        Motivo = motivo,
        Destinatario = "gestor@rotalog.com",
        Mensagem = "Veículo ABC1D23 ultrapassou 10000 km. Agendar manutenção preventiva."
    };

    private static NotificacaoResponse Resposta(string status, long id = 1) => new()
    {
        Id = id,
        Tipo = AlertaManutencaoService.Tipo,
        Canal = "email",
        Destinatario = "gestor@rotalog.com",
        Mensagem = "m",
        Status = status,
        Tentativas = 1,
        ErroMensagem = status == "ENVIADO" ? null : "SMTP connection timeout (simulated)"
    };

    private static AlertaManutencaoService NovoService(NotificacoesDbContext context, Mock<NotificacaoService> notificacao) =>
        new(context, notificacao.Object, NullLogger<AlertaManutencaoService>.Instance);

    [Fact]
    public async Task AlertaNovo_CriaNotificacaoDeEmailParaOGestorComReferenciaDoAlerta()
    {
        using var context = NovoContexto();
        var notificacao = NovoNotificacaoServiceMock(context);
        NotificacaoRequest? enviado = null;
        notificacao.Setup(n => n.CriarNotificacao(It.IsAny<NotificacaoRequest>()))
            .Callback<NotificacaoRequest>(r => enviado = r)
            .ReturnsAsync(Resposta("ENVIADO"));

        var resultado = await NovoService(context, notificacao).Processar(Pedido());

        Assert.True(resultado.Enviada);
        Assert.False(resultado.JaEnviadaAnteriormente);
        Assert.NotNull(enviado);
        Assert.Equal("ALERTA_MANUTENCAO_PREVENTIVA", enviado!.Tipo);
        Assert.Equal("email", enviado.Canal);
        Assert.Equal("gestor@rotalog.com", enviado.Destinatario);
        Assert.Equal("api-frotas", enviado.ServicoOrigem);
        Assert.Equal("alerta-manutencao-7", enviado.ReferenciaId);
        Assert.Contains("ABC1D23", enviado.Assunto);
        Assert.Equal("ABC1D23", enviado.Variaveis!["placa"]);
        Assert.Equal("Quilometragem acima do limite desde a última manutenção", enviado.Variaveis["motivo"]);
    }

    [Fact]
    public async Task EmailNaoEnviado_RetornaNaoEnviadaParaOChamadorManterPendente()
    {
        using var context = NovoContexto();
        var notificacao = NovoNotificacaoServiceMock(context);
        notificacao.Setup(n => n.CriarNotificacao(It.IsAny<NotificacaoRequest>()))
            .ReturnsAsync(Resposta("PENDENTE"));

        var resultado = await NovoService(context, notificacao).Processar(Pedido());

        Assert.False(resultado.Enviada);
        Assert.Equal("PENDENTE", resultado.Notificacao.Status);
    }

    [Fact]
    public async Task AlertaJaEnviado_NaoEnviaOutroEmail()
    {
        using var context = NovoContexto();
        context.Notificacoes.Add(NotificacaoExistente(10, "ENVIADO"));
        await context.SaveChangesAsync();
        var notificacao = NovoNotificacaoServiceMock(context);
        notificacao.Setup(n => n.BuscarPorId(10)).ReturnsAsync(Resposta("ENVIADO", 10));

        var resultado = await NovoService(context, notificacao).Processar(Pedido());

        Assert.True(resultado.Enviada);
        Assert.True(resultado.JaEnviadaAnteriormente);
        notificacao.Verify(n => n.CriarNotificacao(It.IsAny<NotificacaoRequest>()), Times.Never);
        notificacao.Verify(n => n.ReenviarNotificacao(It.IsAny<long>(), It.IsAny<ReenvioRequest?>()), Times.Never);
    }

    [Theory]
    [InlineData("PENDENTE")]
    [InlineData("FALHA")]
    public async Task AlertaQueNaoFoiEnviado_ReaproveitaANotificacaoEReenvia(string statusAnterior)
    {
        using var context = NovoContexto();
        context.Notificacoes.Add(NotificacaoExistente(10, statusAnterior));
        await context.SaveChangesAsync();
        var notificacao = NovoNotificacaoServiceMock(context);
        ReenvioRequest? reenvio = null;
        notificacao.Setup(n => n.ReenviarNotificacao(10, It.IsAny<ReenvioRequest?>()))
            .Callback<long, ReenvioRequest?>((_, r) => reenvio = r)
            .ReturnsAsync(Resposta("ENVIADO", 10));

        var resultado = await NovoService(context, notificacao).Processar(Pedido());

        Assert.True(resultado.Enviada);
        Assert.Equal("gestor@rotalog.com", reenvio?.NovoDestinatario);
        notificacao.Verify(n => n.CriarNotificacao(It.IsAny<NotificacaoRequest>()), Times.Never);
    }

    [Fact]
    public async Task AlertasDiferentes_NaoCompartilhamNotificacao()
    {
        using var context = NovoContexto();
        context.Notificacoes.Add(NotificacaoExistente(10, "ENVIADO", "alerta-manutencao-99"));
        await context.SaveChangesAsync();
        var notificacao = NovoNotificacaoServiceMock(context);
        notificacao.Setup(n => n.CriarNotificacao(It.IsAny<NotificacaoRequest>()))
            .ReturnsAsync(Resposta("ENVIADO", 11));

        var resultado = await NovoService(context, notificacao).Processar(Pedido(alertaId: 7));

        Assert.False(resultado.JaEnviadaAnteriormente);
        notificacao.Verify(n => n.CriarNotificacao(It.IsAny<NotificacaoRequest>()), Times.Once);
    }

    [Theory]
    [InlineData("XPTO")]
    [InlineData("")]
    [InlineData("quilometragem")]
    public async Task MotivoInvalido_LancaArgumentException(string motivo)
    {
        using var context = NovoContexto();
        var notificacao = NovoNotificacaoServiceMock(context);

        await Assert.ThrowsAsync<ArgumentException>(() =>
            NovoService(context, notificacao).Processar(Pedido(motivo: motivo)));

        notificacao.Verify(n => n.CriarNotificacao(It.IsAny<NotificacaoRequest>()), Times.Never);
    }

    [Theory]
    [InlineData("QUILOMETRAGEM", "Quilometragem acima do limite")]
    [InlineData("TEMPO", "Tempo acima do limite")]
    [InlineData("QUILOMETRAGEM_E_TEMPO", "Quilometragem e tempo acima dos limites")]
    public async Task TodosOsMotivosSaoTraduzidosParaTextoLegivel(string motivo, string trechoEsperado)
    {
        using var context = NovoContexto();
        var notificacao = NovoNotificacaoServiceMock(context);
        NotificacaoRequest? enviado = null;
        notificacao.Setup(n => n.CriarNotificacao(It.IsAny<NotificacaoRequest>()))
            .Callback<NotificacaoRequest>(r => enviado = r)
            .ReturnsAsync(Resposta("ENVIADO"));

        await NovoService(context, notificacao).Processar(Pedido(motivo: motivo));

        Assert.Contains(trechoEsperado, enviado!.Variaveis!["motivo"]);
    }

    /// <summary>
    /// Com o NotificacaoService real e o template do seed, o e-mail é montado com placa, critério e mensagem.
    /// (O envio em si é simulado com falha aleatória, por isso só o conteúdo é verificado.)
    /// </summary>
    [Fact]
    public async Task ComTemplateDoSeed_EmailTemAssuntoECorpoDoAlerta()
    {
        using var context = NovoContexto();
        context.Templates.Add(new TemplateNotificacao
        {
            Tipo = "ALERTA_MANUTENCAO_PREVENTIVA",
            Canal = "email",
            AssuntoTemplate = "ALERTA: Manutenção preventiva - Veículo {{placa}}",
            CorpoTemplate = "{{mensagem}}\n\nVeículo: {{placa}}\nCritério: {{motivo}}\n\nEquipe RotaLog",
            Ativo = true
        });
        await context.SaveChangesAsync();
        var real = new NotificacaoService(context, NullLogger<NotificacaoService>.Instance,
            new ConfigurationBuilder().Build());

        await new AlertaManutencaoService(context, real, NullLogger<AlertaManutencaoService>.Instance)
            .Processar(Pedido());

        var salva = await context.Notificacoes.SingleAsync();
        Assert.Equal("ALERTA: Manutenção preventiva - Veículo ABC1D23", salva.Assunto);
        Assert.Contains("Veículo: ABC1D23", salva.Mensagem);
        Assert.Contains("Critério: Quilometragem acima do limite", salva.Mensagem);
        Assert.Contains("Agendar manutenção preventiva.", salva.Mensagem);
        Assert.Equal("alerta-manutencao-7", salva.ReferenciaId);
        Assert.Equal("api-frotas", salva.ServicoOrigem);
        Assert.Equal("gestor@rotalog.com", salva.Destinatario);
    }

    private static Notificacao NotificacaoExistente(long id, string status, string referencia = "alerta-manutencao-7") => new()
    {
        Id = id,
        Tipo = AlertaManutencaoService.Tipo,
        Canal = "email",
        Destinatario = "gestor@rotalog.com",
        Mensagem = "m",
        Status = status,
        ServicoOrigem = AlertaManutencaoService.ServicoOrigem,
        ReferenciaId = referencia
    };
}
