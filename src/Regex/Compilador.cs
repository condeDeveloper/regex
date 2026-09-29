namespace Conde.Regex;

/// <summary>
/// Transforma a árvore no programa que a máquina executa.
/// </summary>
/// <remarks>
/// <para>
/// A tradução de cada forma cabe em três linhas, e é a construção de Thompson
/// tal como ele a publicou:
/// </para>
/// <code>
///   a           casar a
///
///   ab          [a][b]
///
///   a|b         dividir L1, L2
///               L1: [a]
///                   pular fim
///               L2: [b]
///               fim:
///
///   a*          L1: dividir L2, fim
///               L2: [a]
///                   pular L1
///               fim:
/// </code>
/// <para>
/// A ordem dos dois alvos do <c>dividir</c> é toda a diferença entre guloso e
/// preguiçoso. No guloso, o caminho que entra na repetição vem primeiro e tem
/// prioridade; no preguiçoso, o que sai. A máquina não sabe de ganância
/// nenhuma — ela só respeita a ordem, e a ordem foi decidida aqui.
/// </para>
/// <para>
/// É a parte do projeto que mais me surpreendeu ao escrever: "guloso" e
/// "preguiçoso", que na documentação de qualquer linguagem são explicados com
/// parágrafos e exemplos, são <b>a troca de dois argumentos</b>.
/// </para>
/// </remarks>
public sealed class Compilador
{
    /// <summary>
    /// A volta do laço é uma divisão nova (padrão) ou um pulo para a entrada?
    /// </summary>
    /// <remarks>
    /// <para>
    /// É a única chave de configuração deste motor, e ela existe para a
    /// ferramenta de placar poder <b>medir</b> as duas construções em vez de eu
    /// escolher pelo argumento mais convincente.
    /// </para>
    /// <para>
    /// O resultado, em 48 mil comparações contra o .NET:
    /// </para>
    /// <code>
    ///   pulo de volta     99,785% das correspondências, 99,537% das capturas
    ///   divisão nova      99,990%                       99,931%
    /// </code>
    /// <para>
    /// As duas parecem certas lendo o código. Uma delas está cinco vezes mais
    /// perto do juiz, e não havia como saber qual sem rodar.
    /// </para>
    /// </remarks>
    public static bool VoltaComoDivisao { get; set; } = true;

    private readonly List<Instrucao> _instrucoes = [];

    public static Programa Compilar(No arvore, int quantosGrupos)
    {
        var compilador = new Compilador();

        // Os compartimentos 0 e 1 guardam o começo e o fim da correspondência
        // inteira. Tratar a expressão toda como o "grupo zero" faz o resto do
        // código não precisar de um caso especial para ela.
        compilador.Emitir(new Instrucao(Operacao.Guardar, Compartimento: 0));
        compilador.Gerar(arvore);
        compilador.Emitir(new Instrucao(Operacao.Guardar, Compartimento: 1));
        compilador.Emitir(new Instrucao(Operacao.Parar));

        return new Programa(compilador._instrucoes, quantosGrupos);
    }

    private int Emitir(Instrucao instrucao)
    {
        _instrucoes.Add(instrucao);

        return _instrucoes.Count - 1;
    }

    private int Proxima => _instrucoes.Count;

    private void Gerar(No no)
    {
        switch (no)
        {
            case No.Vazio:
                break;

            case No.Classe classe:
                Emitir(new Instrucao(Operacao.Casar, classe.Conjunto));
                break;

            case No.Ancora ancora:
                Emitir(new Instrucao(Operacao.Conferir, Ancora: ancora.Tipo));
                break;

            case No.Sequencia sequencia:
                foreach (var parte in sequencia.Partes)
                {
                    Gerar(parte);
                }

                break;

            case No.Grupo grupo:
                // Os compartimentos de um grupo são 2n e 2n+1. O grupo 1 usa o
                // 2 e o 3, o grupo 2 usa o 4 e o 5, e assim por diante.
                Emitir(new Instrucao(Operacao.Guardar, Compartimento: grupo.Numero * 2));
                Gerar(grupo.Dentro);
                Emitir(new Instrucao(Operacao.Guardar, Compartimento: grupo.Numero * 2 + 1));
                break;

            case No.Alternativa alternativa:
                GerarAlternativa(alternativa);
                break;

            case No.Repeticao repeticao:
                GerarRepeticao(repeticao);
                break;

            case No.Opcional opcional:
                GerarOpcional(opcional);
                break;

            default:
                throw new InvalidOperationException($"nó desconhecido: {no.GetType().Name}");
        }
    }

    private void GerarAlternativa(No.Alternativa alternativa)
    {
        var divisao = Emitir(new Instrucao(Operacao.Dividir));

        // A esquerda começa logo depois do dividir e tem prioridade. É por isso
        // que `a|ab` casa só "a" em "ab": a primeira alternativa que der certo
        // vence, e nenhuma implementação com retrocesso faz diferente.
        _instrucoes[divisao] = _instrucoes[divisao] with { Alvo = Proxima };

        Gerar(alternativa.Esquerda);

        var pulo = Emitir(new Instrucao(Operacao.Pular));

        _instrucoes[divisao] = _instrucoes[divisao] with { Outro = Proxima };

        Gerar(alternativa.Direita);

        _instrucoes[pulo] = _instrucoes[pulo] with { Alvo = Proxima };
    }

    /// <summary>
    /// A repetição, com a volta escrita como uma <b>segunda</b> divisão.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A construção clássica fecha o laço com um <c>pular</c> de volta para a
    /// divisão de entrada. Funciona, e discorda do .NET num caso: quando o
    /// corpo casa o vazio.
    /// </para>
    /// <para>
    /// O motivo é o guarda contra laço infinito. A máquina não segue duas vezes
    /// a mesma instrução no mesmo caractere — senão <c>(a*)*</c> giraria para
    /// sempre —, e o <c>pular</c> de volta cai exatamente na instrução de onde
    /// o laço saiu. Resultado: a iteração que casaria o vazio morre antes de
    /// registrar a captura.
    /// </para>
    /// <para>
    /// Um motor com retrocesso faz o contrário: ele roda a iteração vazia,
    /// guarda o que ela capturou, <b>e só então</b> percebe que não avançou e
    /// para. É por isso que <c>(a*)*</c> contra "aaa" deixa o grupo 1 com
    /// <c>""</c> no .NET e com <c>"aaa"</c> num Thompson ingênuo — e foi o juiz
    /// que apontou, porque lendo o código as duas parecem certas.
    /// </para>
    /// <para>
    /// A saída é uma instrução a mais: a volta é uma divisão <b>nova</b>, com os
    /// mesmos alvos. Sendo outra instrução, ela não está marcada como visitada,
    /// então uma — e só uma — passada vazia acontece. O guarda continua valendo
    /// na segunda, e o laço infinito continua impossível.
    /// </para>
    /// </remarks>
    private void GerarRepeticao(No.Repeticao repeticao)
    {
        var entrada = Emitir(new Instrucao(Operacao.Dividir));

        var corpo = Proxima;

        Gerar(repeticao.Dentro);

        var volta = VoltaComoDivisao
            ? Emitir(new Instrucao(Operacao.Dividir))
            : Emitir(new Instrucao(Operacao.Pular, Alvo: entrada));

        var saida = Proxima;

        // Aqui está a ganância inteira: no guloso, entrar no corpo é a primeira
        // opção; no preguiçoso, sair é. As duas divisões levam a mesma ordem.
        foreach (var onde in VoltaComoDivisao ? (int[])[entrada, volta] : [entrada])
        {
            _instrucoes[onde] = repeticao.Guloso
                ? _instrucoes[onde] with { Alvo = corpo, Outro = saida }
                : _instrucoes[onde] with { Alvo = saida, Outro = corpo };
        }
    }

    private void GerarOpcional(No.Opcional opcional)
    {
        var divisao = Emitir(new Instrucao(Operacao.Dividir));
        var corpo = Proxima;

        Gerar(opcional.Dentro);

        var saida = Proxima;

        _instrucoes[divisao] = opcional.Guloso
            ? _instrucoes[divisao] with { Alvo = corpo, Outro = saida }
            : _instrucoes[divisao] with { Alvo = saida, Outro = corpo };
    }
}
