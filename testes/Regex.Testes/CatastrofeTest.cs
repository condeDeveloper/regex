using System.Diagnostics;
using Xunit;
using Dotnet = System.Text.RegularExpressions;

namespace Conde.Regex.Testes;

/// <summary>
/// A catástrofe que não acontece aqui.
/// </summary>
/// <remarks>
/// <para>
/// Este é o teste que justifica o projeto inteiro. Um motor com retrocesso
/// diante de <c>(a+)+b</c> e de trinta letras "a" tenta <b>todas</b> as formas
/// de dividir as trinta letras entre os dois <c>+</c> antes de desistir. São
/// 2³⁰ caminhos, e cada letra a mais dobra o número.
/// </para>
/// <para>
/// Isso tem nome — ReDoS — e conta história. Em 20 de julho de 2016 o Stack
/// Overflow saiu do ar por 34 minutos porque uma linha de resposta com muitos
/// espaços no fim encontrou uma expressão dessas. Em 2 de julho de 2019 a
/// Cloudflare tirou boa parte da internet do ar por 27 minutos pelo mesmo
/// motivo: uma expressão nova numa regra de firewall, com um <c>.*.*=.*</c>
/// dentro.
/// </para>
/// <para>
/// Aqui a mesma expressão roda em microssegundos. <b>Não por otimização</b>: por
/// não haver caminho para explodir. A máquina anda por todos os caminhos ao
/// mesmo tempo, e "todos os caminhos" cabem no tamanho do programa.
/// </para>
/// <para>
/// O preço está dito no README: não há retrovisor (<c>\1</c>), não há
/// olhar-adiante, não há retrocesso atômico. Não é falta de vontade — uma
/// expressão com retrovisor não é regular, e casá-la é NP-completo. Um motor
/// que os oferece está oferecendo a catástrofe junto.
/// </para>
/// </remarks>
public class CatastrofeTest
{
    private static TimeSpan Cronometrar(Action acao)
    {
        var relogio = Stopwatch.StartNew();

        acao();

        return relogio.Elapsed;
    }

    [Theory(DisplayName = "as expressões que derrubam um motor com retrocesso")]
    [InlineData("(a+)+b", 40)]
    [InlineData("(a|a)*b", 40)]
    [InlineData("(a*)*b", 40)]
    [InlineData("(a|aa)+b", 40)]
    [InlineData("(.*)*b", 40)]
    [InlineData("a?a?a?a?a?a?a?a?a?a?a?a?a?a?a?a?a?a?a?a?aaaaaaaaaaaaaaaaaaaa", 0)]
    public void NaoExplode(string expressao, int quantosA)
    {
        var texto = new string('a', quantosA > 0 ? quantosA : 40);

        var minha = new Expressao(expressao);

        var gasto = Cronometrar(() => minha.Casa(texto));

        Assert.True(gasto < TimeSpan.FromSeconds(1),
            $"/{expressao}/ em {texto.Length} letras levou {gasto.TotalMilliseconds:F0} ms");
    }

    [Fact(DisplayName = "o tempo cresce com o texto, e não com o dobro dele")]
    public void OCustoEhLinear()
    {
        // A promessa da máquina é custo proporcional ao texto vezes o tamanho da
        // expressão. Este teste não mede a constante -- mede o formato da curva:
        // dobrar o texto tem de mais ou menos dobrar o tempo, e não quadruplicar
        // nem elevar ao quadrado.
        var expressao = new Expressao("(a+)+b");

        var curto = new string('a', 2_000);
        var longo = new string('a', 4_000);

        // Uma passada de aquecimento: sem ela, a primeira medição carrega o
        // custo de compilar o método, que é maior que o que se quer medir.
        expressao.Casa(curto);

        var gastoCurto = Cronometrar(() => expressao.Casa(curto));
        var gastoLongo = Cronometrar(() => expressao.Casa(longo));

        var razao = gastoLongo.TotalMilliseconds
            / Math.Max(0.001, gastoCurto.TotalMilliseconds);

        Assert.True(razao < 8,
            $"dobrar o texto multiplicou o tempo por {razao:F1}: "
            + $"{gastoCurto.TotalMilliseconds:F1} ms contra {gastoLongo.TotalMilliseconds:F1} ms");
    }

    [Fact(DisplayName = "o .NET realmente sofre no mesmo caso, e este motor não")]
    public void OJuizConfirmaQueAAmeacaEhReal()
    {
        // Não basta afirmar que um motor com retrocesso explodiria: dá para
        // mostrar. O .NET tem um tempo limite justamente por causa disto, e aqui
        // ele é posto em meio segundo -- tempo de sobra para qualquer expressão
        // sadia e nem perto do necessário para esta.
        //
        // Se um dia o .NET passar a usar uma máquina sem retrocesso por padrão
        // e este teste deixar de estourar, ele avisa: o `Assert` é sobre o meu
        // motor, e o resto é informação.
        const string expressao = "(a+)+$";

        var texto = new string('a', 40) + "!";

        var doNet = new Dotnet.Regex(expressao, Dotnet.RegexOptions.None,
            TimeSpan.FromMilliseconds(500));

        var estourou = false;

        try
        {
            doNet.IsMatch(texto);
        }
        catch (Dotnet.RegexMatchTimeoutException)
        {
            estourou = true;
        }

        var minha = new Expressao(expressao);
        var gasto = Cronometrar(() => minha.Casa(texto));

        Assert.True(gasto < TimeSpan.FromMilliseconds(100),
            $"este motor levou {gasto.TotalMilliseconds:F0} ms — devia ser instantâneo");

        // O resultado tem de ser o mesmo, estourando ou não o tempo lá.
        if (!estourou)
        {
            Assert.Equal(doNet.IsMatch(texto), minha.Casa(texto));
        }
    }

    [Fact(DisplayName = "um texto grande com uma expressão grande")]
    public void TextoGrande()
    {
        var expressao = new Expressao(@"(\w+)@(\w+)\.(\w+)");

        var texto = string.Join(" ",
            Enumerable.Range(0, 20_000).Select(i => $"palavra{i}"))
            + " miguel@exemplo.com fim";

        var gasto = Cronometrar(() =>
        {
            var achado = expressao.Procurar(texto);

            Assert.True(achado.Sucesso);
            Assert.Equal("miguel@exemplo.com", achado.Valor);
        });

        Assert.True(gasto < TimeSpan.FromSeconds(10),
            $"levou {gasto.TotalSeconds:F1} s num texto de {texto.Length} caracteres");
    }

    [Fact(DisplayName = "a repetição gigante é recusada em vez de aceita")]
    public void RepeticaoGigante()
    {
        // `a{2000000000}` são catorze caracteres e geraria dois bilhões de
        // instruções. Recusar com mensagem é a única resposta possível -- e é
        // o tipo de entrada que chega de fora em qualquer serviço que aceite
        // expressão de usuário.
        var erro = Assert.Throws<ErroDeExpressao>(() => new Expressao("a{2000000000}"));

        Assert.Contains("repetição", erro.Message);

        // E o que cabe, cabe.
        var grande = new Expressao("a{5000}");

        Assert.True(grande.Casa(new string('a', 5_000)));
        Assert.False(grande.Casa(new string('a', 4_999)));
    }
}
