namespace Conde.Regex;

/// <summary>
/// A máquina: executa o programa sobre o texto, todos os caminhos de uma vez.
/// </summary>
/// <remarks>
/// <para>
/// É a "Pike VM", de Rob Pike — a mesma máquina que o <c>regexp</c> do Go e o
/// RE2 do Google usam. A ideia que a separa de um motor com retrocesso é uma
/// só, e é grande:
/// </para>
/// <para>
/// <b>Em vez de tentar um caminho e voltar, anda por todos ao mesmo tempo.</b>
/// A cada caractere do texto há um conjunto de posições possíveis no programa,
/// e o passo consiste em avançar todas elas de uma vez. Um caractere é lido
/// <b>uma vez</b>, e o custo é o tamanho do texto vezes o tamanho da expressão.
/// </para>
/// <para>
/// A consequência prática é o que faz este projeto valer a pena: o
/// <c>(a+)+b</c> contra sessenta letras "a", que trava um motor com retrocesso
/// por horas — a catástrofe que derrubou a Cloudflare em 2019 e o Stack
/// Overflow em 2016 —, roda aqui em microssegundos. Não por otimização: por não
/// haver caminho para explodir. Há um teste que mede isso.
/// </para>
/// <para>
/// O preço é que não dá para ter retrovisor (<c>\1</c>), e não dá mesmo: uma
/// expressão com retrovisor não é mais regular, e casá-la é NP-completo. Um
/// motor que os oferece está oferecendo junto a catástrofe.
/// </para>
/// </remarks>
public sealed class Maquina
{
    private readonly Programa _programa;

    public Maquina(Programa programa)
    {
        _programa = programa;
    }

    /// <summary>Uma linha de execução: onde está no programa, e o que já capturou.</summary>
    private readonly record struct Linha(int Ponto, int[] Compartimentos);

    /// <summary>
    /// Procura a primeira correspondência a partir de <paramref name="de"/>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A busca é "mais à esquerda primeiro": tenta-se casar começando em cada
    /// posição, da esquerda para a direita, e a primeira que der certo vence.
    /// É o que toda implementação faz e o que o .NET faz — e é diferente da
    /// regra do POSIX, que escolhe a correspondência <b>mais longa</b>. As duas
    /// são defensáveis, e ser igual ao juiz é o que este projeto se propôs.
    /// </para>
    /// </remarks>
    public int[]? Procurar(string texto, int de)
    {
        for (var comeco = de; comeco <= texto.Length; comeco++)
        {
            var achado = Rodar(texto, comeco);

            if (achado is not null)
            {
                return achado;
            }
        }

        return null;
    }

    /// <summary>Executa o programa a partir de uma posição fixa do texto.</summary>
    private int[]? Rodar(string texto, int comeco)
    {
        var quantas = _programa.Instrucoes.Count;

        var atuais = new List<Linha>(quantas);
        var proximas = new List<Linha>(quantas);

        // Um número de geração por instrução, em vez de um vetor de booleanos
        // que precisaria ser limpo a cada caractere. É o truque que faz o laço
        // custar O(programa) por caractere em vez de O(programa) mais a limpeza.
        var visitadas = new int[quantas];
        var geracao = 0;

        int[]? melhor = null;

        var vazios = new int[_programa.Compartimentos];

        Array.Fill(vazios, -1);

        geracao++;
        Acrescentar(atuais, visitadas, geracao, 0, comeco, texto, vazios);

        for (var posicao = comeco; ; posicao++)
        {
            if (atuais.Count == 0)
            {
                break;
            }

            geracao++;
            proximas.Clear();

            var parou = false;

            // A ordem da lista é a ordem de prioridade, e percorrê-la em ordem
            // é o que faz a semântica bater com a de um motor com retrocesso.
            for (var i = 0; i < atuais.Count && !parou; i++)
            {
                var linha = atuais[i];
                var instrucao = _programa.Instrucoes[linha.Ponto];

                switch (instrucao.Operacao)
                {
                    case Operacao.Casar:
                        if (posicao < texto.Length
                            && instrucao.Conjunto!.Contem(texto[posicao]))
                        {
                            Acrescentar(proximas, visitadas, geracao,
                                linha.Ponto + 1, posicao + 1, texto, linha.Compartimentos);
                        }

                        break;

                    case Operacao.Parar:
                        // Achou. Como a lista está em ordem de prioridade, esta
                        // é a melhor correspondência que existe a partir deste
                        // começo -- e todas as linhas de prioridade MENOR que
                        // ainda estão na fila são descartadas.
                        //
                        // Descartá-las não é otimização: é a semântica. Sem
                        // isso, uma alternativa posterior que casasse mais
                        // adiante venceria, e `a|ab` casaria "ab".
                        melhor = linha.Compartimentos;
                        parou = true;
                        break;

                    default:
                        throw new InvalidOperationException(
                            $"{instrucao.Operacao} devia ter sido resolvida no fecho");
                }
            }

            if (posicao >= texto.Length)
            {
                break;
            }

            (atuais, proximas) = (proximas, atuais);
        }

        return melhor;
    }

    /// <summary>
    /// Põe uma linha na lista, resolvendo tudo o que não consome caractere.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Chama-se "fecho épsilon": a partir de um ponto, seguem-se todos os
    /// pulos, divisões, capturas e âncoras até chegar numa instrução que de
    /// fato consome — e só essas entram na lista.
    /// </para>
    /// <para>
    /// O <c>visitadas</c> é o que impede um laço infinito em expressões como
    /// <c>(a*)*</c>, em que o corpo pode casar o vazio e voltar ao começo para
    /// sempre. Uma instrução já vista nesta geração não é seguida de novo, e a
    /// primeira visita é a de maior prioridade — o que é exatamente o que se
    /// quer manter.
    /// </para>
    /// </remarks>
    private void Acrescentar(List<Linha> lista, int[] visitadas, int geracao,
        int ponto, int posicao, string texto, int[] compartimentos)
    {
        // Uma pilha explícita em vez de recursão: uma expressão com dez mil
        // repetições geraria dez mil quadros e derrubaria a pilha do processo.
        var pendentes = new Stack<(int Ponto, int[] Compartimentos)>();

        pendentes.Push((ponto, compartimentos));

        while (pendentes.Count > 0)
        {
            var (onde, atuais) = pendentes.Pop();

            if (visitadas[onde] == geracao)
            {
                continue;
            }

            visitadas[onde] = geracao;

            var instrucao = _programa.Instrucoes[onde];

            switch (instrucao.Operacao)
            {
                case Operacao.Pular:
                    pendentes.Push((instrucao.Alvo, atuais));
                    break;

                case Operacao.Dividir:
                    // O segundo entra primeiro na pilha para sair por último:
                    // a ordem de prioridade tem de ser a de exploração.
                    pendentes.Push((instrucao.Outro, atuais));
                    pendentes.Push((instrucao.Alvo, atuais));
                    break;

                case Operacao.Guardar:
                {
                    // A cópia é necessária: duas linhas podem partir do mesmo
                    // ponto e capturar coisas diferentes. Sem ela, uma
                    // sobrescreve a captura da outra e as capturas saem trocadas
                    // -- um defeito que não aparece no `IsMatch` e estraga todo
                    // `Groups[1]`.
                    var copia = (int[])atuais.Clone();

                    copia[instrucao.Compartimento] = posicao;

                    pendentes.Push((onde + 1, copia));
                    break;
                }

                case Operacao.Conferir:
                    if (Confere(instrucao.Ancora, texto, posicao))
                    {
                        pendentes.Push((onde + 1, atuais));
                    }

                    break;

                default:
                    lista.Add(new Linha(onde, atuais));
                    break;
            }
        }
    }

    private static bool Confere(TipoDeAncora tipo, string texto, int posicao) => tipo switch
    {
        TipoDeAncora.Comeco => posicao == 0,
        TipoDeAncora.Fim => EhFim(texto, posicao),
        TipoDeAncora.Fronteira => EhFronteira(texto, posicao),
        TipoDeAncora.ForaDeFronteira => !EhFronteira(texto, posicao),
        _ => false,
    };

    /// <summary>
    /// O <c>$</c> casa no fim do texto <b>ou logo antes de uma quebra final</b>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Esta é uma das regras mais surpreendentes de todo o assunto, e eu não a
    /// conhecia: <c>/$/</c> contra uma quebra de linha sozinha casa na
    /// posição <b>0</b>, e não
    /// na 1. Escrevi a âncora como "posição igual ao tamanho", que é o que
    /// qualquer pessoa escreveria, e o juiz apontou na hora.
    /// </para>
    /// <para>
    /// A regra vem do Perl, e vem de antes dele: as ferramentas de linha —
    /// <c>grep</c>, <c>sed</c>, <c>awk</c> — trabalhavam linha a linha e a
    /// quebra final era um detalhe do arquivo, não do conteúdo. Quando as
    /// expressões passaram a rodar sobre textos inteiros, o comportamento foi
    /// mantido para que <c>/erro$/</c> continuasse casando uma linha lida com a
    /// quebra dentro.
    /// </para>
    /// <para>
    /// Quem quer o fim de verdade usa <c>\z</c>. Este motor ainda não o tem, e
    /// isso está dito no README.
    /// </para>
    /// </remarks>
    private static bool EhFim(string texto, int posicao) =>
        posicao == texto.Length
        || (posicao == texto.Length - 1 && texto[posicao] == '\n');

    /// <summary>
    /// A fronteira de palavra: de um lado um caractere de palavra, do outro não.
    /// </summary>
    /// <remarks>
    /// Ela não consome nada — é uma afirmação sobre o ponto entre dois
    /// caracteres. O começo e o fim do texto contam como "não é palavra", e é
    /// por isso que <c>\bfoo\b</c> casa a linha inteira "foo".
    /// </remarks>
    private static bool EhFronteira(string texto, int posicao)
    {
        var antes = posicao > 0 && ConjuntoDeCaracteres.EhDePalavra(texto[posicao - 1]);
        var depois = posicao < texto.Length
            && ConjuntoDeCaracteres.EhDePalavra(texto[posicao]);

        return antes != depois;
    }
}
