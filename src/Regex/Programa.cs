using System.Text;

namespace Conde.Regex;

/// <summary>
/// As cinco instruções de uma expressão regular compilada.
/// </summary>
/// <remarks>
/// <para>
/// A ideia é de Ken Thompson, 1968: uma expressão regular não precisa ser
/// interpretada como árvore — ela vira um <b>programa</b>, e casar é executar
/// esse programa. Thompson compilava para código de máquina do IBM 7094; aqui
/// são cinco instruções e um laço.
/// </para>
/// <para>
/// Cinco bastam, e é o que torna o resto do projeto pequeno:
/// </para>
/// <list type="table">
///   <item><term>Casar</term><description>consome um caractere se ele estiver no conjunto</description></item>
///   <item><term>Pular</term><description>vai para outro ponto do programa</description></item>
///   <item><term>Dividir</term><description>segue por dois caminhos, o primeiro com prioridade</description></item>
///   <item><term>Guardar</term><description>anota a posição atual num compartimento de captura</description></item>
///   <item><term>Conferir</term><description>exige uma condição sem consumir nada</description></item>
/// </list>
/// <para>
/// O <c>Dividir</c> é a única que faz algo estranho para quem vem de linguagem
/// comum: ela não escolhe, ela vai pelos dois. É isso que dispensa o
/// retrocesso — em vez de tentar um caminho e voltar, a máquina anda por todos
/// ao mesmo tempo.
/// </para>
/// </remarks>
public enum Operacao
{
    Casar,
    Pular,
    Dividir,
    Guardar,
    Conferir,
    Parar,
}

public readonly record struct Instrucao(
    Operacao Operacao,
    ConjuntoDeCaracteres? Conjunto = null,
    int Alvo = 0,
    int Outro = 0,
    int Compartimento = 0,
    TipoDeAncora Ancora = TipoDeAncora.Comeco)
{
    public override string ToString() => Operacao switch
    {
        Operacao.Casar => $"casar    {Conjunto}",
        Operacao.Pular => $"pular    {Alvo}",
        Operacao.Dividir => $"dividir  {Alvo}, {Outro}",
        Operacao.Guardar => $"guardar  {Compartimento}",
        Operacao.Conferir => $"conferir {Ancora}",
        _ => "parar",
    };
}

/// <summary>Um programa compilado, pronto para a máquina executar.</summary>
public sealed class Programa(IReadOnlyList<Instrucao> instrucoes, int quantosGrupos)
{
    public IReadOnlyList<Instrucao> Instrucoes { get; } = instrucoes;

    public int QuantosGrupos { get; } = quantosGrupos;

    /// <summary>
    /// Quantos compartimentos de captura há: dois por grupo, mais dois para a
    /// correspondência inteira.
    /// </summary>
    public int Compartimentos => (QuantosGrupos + 1) * 2;

    public override string ToString()
    {
        var texto = new StringBuilder();

        for (var i = 0; i < Instrucoes.Count; i++)
        {
            texto.AppendLine($"{i,4}  {Instrucoes[i]}");
        }

        return texto.ToString();
    }
}
