using System.ComponentModel.DataAnnotations;
using api_notificacoes.Controllers;
using api_notificacoes.Data;
using api_notificacoes.DTOs;
using api_notificacoes.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace api_notificacoes.Tests;

public class AlertasManutencaoControllerTests
{
    private static Mock<AlertaManutencaoService> NovoServiceMock()
    {
        var options = new DbContextOptionsBuilder<NotificacoesDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options;
        var context = new NotificacoesDbContext(options);
        var notificacao = new Mock<NotificacaoService>(context, NullLogger<NotificacaoService>.Instance,
            new Microsoft.Extensions.Configuration.ConfigurationBuilder().Build());
        return new Mock<AlertaManutencaoService>(context, notificacao.Object,
            NullLogger<AlertaManutencaoService>.Instance);
    }

    private static AlertasManutencaoController NovoController(Mock<AlertaManutencaoService> service) =>
        new(service.Object, NullLogger<AlertasManutencaoController>.Instance);

    private static AlertaManutencaoRequest Pedido() => new()
    {
        AlertaId = 7,
        VeiculoId = 3,
        Placa = "ABC1D23",
        Motivo = "TEMPO",
        Destinatario = "gestor@rotalog.com",
        Mensagem = "mensagem"
    };

    private static NotificacaoResponse Notificacao(string status) => new()
    {
        Id = 5,
        Tipo = "ALERTA_MANUTENCAO_PREVENTIVA",
        Canal = "email",
        Destinatario = "gestor@rotalog.com",
        Mensagem = "m",
        Status = status
    };

    [Fact]
    public async Task EmailEnviado_Retorna201()
    {
        var service = NovoServiceMock();
        service.Setup(s => s.Processar(It.IsAny<AlertaManutencaoRequest>()))
            .ReturnsAsync(new ResultadoAlertaManutencao(Notificacao("ENVIADO"), true, false));

        var resultado = await NovoController(service).Criar(Pedido());

        var objeto = Assert.IsType<ObjectResult>(resultado);
        Assert.Equal(201, objeto.StatusCode);
        Assert.Equal("ENVIADO", Assert.IsType<NotificacaoResponse>(objeto.Value).Status);
    }

    [Fact]
    public async Task AlertaJaEnviadoAntes_Retorna200()
    {
        var service = NovoServiceMock();
        service.Setup(s => s.Processar(It.IsAny<AlertaManutencaoRequest>()))
            .ReturnsAsync(new ResultadoAlertaManutencao(Notificacao("ENVIADO"), true, true));

        var resultado = await NovoController(service).Criar(Pedido());

        Assert.IsType<OkObjectResult>(resultado);
    }

    [Fact]
    public async Task EmailNaoEnviado_Retorna503ParaOFrotasManterPendente()
    {
        var service = NovoServiceMock();
        service.Setup(s => s.Processar(It.IsAny<AlertaManutencaoRequest>()))
            .ReturnsAsync(new ResultadoAlertaManutencao(Notificacao("PENDENTE"), false, false));

        var resultado = await NovoController(service).Criar(Pedido());

        var objeto = Assert.IsType<ObjectResult>(resultado);
        Assert.Equal(503, objeto.StatusCode);
        var corpo = Assert.IsType<AlertaNaoEnviadoResponse>(objeto.Value);
        Assert.Equal(5, corpo.NotificacaoId);
        Assert.Equal("PENDENTE", corpo.Status);
    }

    [Fact]
    public async Task PedidoInvalido_Retorna400()
    {
        var service = NovoServiceMock();
        service.Setup(s => s.Processar(It.IsAny<AlertaManutencaoRequest>()))
            .ThrowsAsync(new ArgumentException("Motivo inválido: XPTO"));

        var resultado = await NovoController(service).Criar(Pedido());

        Assert.IsType<BadRequestObjectResult>(resultado);
    }

    [Fact]
    public async Task ErroInesperado_Retorna500SemExporDetalhes()
    {
        var service = NovoServiceMock();
        service.Setup(s => s.Processar(It.IsAny<AlertaManutencaoRequest>()))
            .ThrowsAsync(new InvalidOperationException("detalhe interno do banco"));

        var resultado = await NovoController(service).Criar(Pedido());

        var objeto = Assert.IsType<ObjectResult>(resultado);
        Assert.Equal(500, objeto.StatusCode);
        Assert.DoesNotContain("detalhe interno", objeto.Value!.ToString());
    }

    [Fact]
    public void Dto_PedidoCompletoEValido()
    {
        Assert.Empty(Validar(Pedido()));
    }

    [Theory]
    [InlineData("AlertaId")]
    [InlineData("Placa")]
    [InlineData("Motivo")]
    [InlineData("Destinatario")]
    [InlineData("Mensagem")]
    public void Dto_CamposObrigatoriosAusentes_SaoRejeitados(string campo)
    {
        var pedido = Pedido();
        switch (campo)
        {
            case "AlertaId": pedido.AlertaId = null; break;
            case "Placa": pedido.Placa = ""; break;
            case "Motivo": pedido.Motivo = ""; break;
            case "Destinatario": pedido.Destinatario = ""; break;
            case "Mensagem": pedido.Mensagem = ""; break;
        }

        Assert.Contains(Validar(pedido), r => r.MemberNames.Contains(campo));
    }

    [Fact]
    public void Dto_EmailInvalidoEPlacaLongaSaoRejeitados()
    {
        var pedido = Pedido();
        pedido.Destinatario = "isso-nao-e-email";
        pedido.Placa = "PLACA-MUITO-LONGA";

        var erros = Validar(pedido);

        Assert.Contains(erros, r => r.MemberNames.Contains("Destinatario"));
        Assert.Contains(erros, r => r.MemberNames.Contains("Placa"));
    }

    private static List<ValidationResult> Validar(AlertaManutencaoRequest pedido)
    {
        var erros = new List<ValidationResult>();
        Validator.TryValidateObject(pedido, new ValidationContext(pedido), erros, validateAllProperties: true);
        return erros;
    }
}
