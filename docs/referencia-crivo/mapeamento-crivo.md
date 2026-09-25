# Mapeamento Crivo × Motor de Decisão (Usebens)

Documento de referência que compara a ferramenta **Crivo 4 (TransUnion)** — com
base no manual oficial em `aux/201308_tutorial_crivo.pdf` (texto extraído em
`docs/referencia-crivo/tutorial_crivo4_texto.txt`) e no material público — com o
que já construímos, para guiar a evolução das telas e funcionalidades.

> Objetivo: basear nossas telas e funcionalidades nas do Crivo, priorizando por
> valor × esforço.

---

## 1. Modelo conceitual

### Crivo
Hierarquia: **Pasta → Política → Critério → Regra**.

- **Pasta**: agrupa políticas/critérios por setor, com permissões (isolamento).
- **Política**: composta por **um ou mais critérios**; produz a **decisão final**.
  Tem tipo **PF** ou **PJ**.
- **Critério**: uma **sequência lógica de regras** (análise parcial). Termina num
  **Retorno Padrão**.
- **Regra**: componente básico. Uma **expressão estilo Excel** que gera
  **condições** (Verdadeiro/Falso); cada condição dispara **ações**. Pode ser
  habilitada/desabilitada, colapsada, nomeada. O Crivo sinaliza "regra
  inalcançável".

### Nosso motor (hoje)
Hierarquia: **Política (DecisionFlow) → Versão → grafo de nós/arestas**.

- Sem os níveis **Pasta** e **Critério**.
- O fluxo é um **grafo livre** (canvas), não uma **lista sequencial de regras**.
- Versionamento (rascunho/publicada/arquivada) — o Crivo tem algo equivalente via
  revisão de critérios.

> **Diferença estrutural principal:** o Crivo é uma *lista sequencial de regras
> por critério*; o nosso é um *grafo de nós*. Ambos são executáveis; o grafo é
> mais flexível, a lista é mais guiada e familiar a quem conhece o Crivo.

---

## 2. Menu lateral do editor (o do print do Crivo)

| Item Crivo | Temos? | Observação |
|---|---|---|
| Nova Regra | 🟡 | Temos nós/condições; não a "regra" no formato Crivo |
| Nova Regra **Matriz** | ❌ | Cruzamento de faixas (tabela). Muito usado em crédito |
| Nova Regra **Campeão/Desafiante** | ❌ | Teste A/B controlado |
| Nova Condição | ✅ | Nó de condição |
| Novo Comentário | ❌ | Anotação visual no fluxo |
| **Minhas Variáveis** | ✅ | Variáveis locais (`{variavel}`) |
| **Variáveis Globais** | ❌ | Compartilhadas entre políticas |
| **Fontes de Informação** | ✅ | Catálogo + `[Fonte;Produto;Dado]` (SERASA fake) |
| Informação coletada na fonte | 🟡 | Resolvido em runtime; sem visualização dedicada |
| **Campos** | ✅ | Campos de entrada (`'campo'`) |
| **Objetos de Análise** | ❌ | Encadear sub-análises (ex.: sócios de uma PJ) |
| **Parâmetros de Saída** | 🟡 | Temos desfecho + score; faltam saídas nomeadas |
| **Operadores** | ✅ | Na engine de fórmula |
| **Ações** | 🟡 | Temos score/decisão/anotação; faltam ações formais |

---

## 3. Ações (Crivo) — gap importante

O Crivo separa **contadores** e **ações** de forma rica:

- **Dois contadores independentes**: **Pontos** (scoring) e **Limite** (ex.: limite
  do cartão). Ações: `Adiciona aos pontos`, `Define pontos`, `Adiciona ao limite`,
  `Define limite`. Acesso ao valor: `[Criterio Atual;Pontos]`, `[Criterio Atual;Limite]`.
- **Justificativa**: `Adiciona à justificativa` (append) e `Define justificativa`
  (substitui tudo). Compõem o relatório final da decisão.
- **Delega a decisão a**: chama outro critério e transfere a responsabilidade da
  resposta final.
- Outras: `Define Cor` (sinalização visual), `Redefine Valor do Campo`.

**Nós hoje:** um único acumulador de **score** e mensagens na trilha. Faltam: o
contador **Limite** separado, ações de **justificativa** estruturada, e ações
como cor/redefinir campo.

---

## 4. Objetos de Análise (Crivo) — gap

Permite **encadear critérios/sub-análises** dentro de uma política, inclusive de
tipo diferente (PF dentro de PJ). Exemplos: analisar os **sócios** de uma empresa,
os **motoristas** de um seguro. Cada elemento gera uma operação adicional.
Suporta **disparo sequencial** e **disparo em paralelo** das coletas às fontes.

**Nós hoje:** não temos. Depende de definirmos o conceito (você disse "especificar
depois").

---

## 5. Operação, auditoria e relatórios (Crivo)

- **Análise individual** e **em lote** (submissão de arquivos, layout, monitoração).
- **Código da operação**, relatório em PDF, "Explica Operação".
- **Auditoria**: histórico de decisões, resumo de análise, utilização de fontes,
  evolução dos resultados, fluxo de operações.
- **Listas especiais** (CPF/CNPJ, Placa/Chassi) na base do Crivo.

**Nós hoje:** temos **trilha de auditoria por execução** (`DecisionExecution` +
`ExecutionTrace`) e endpoints de listagem/detalhe. Faltam: operação em lote,
relatórios/BI, listas especiais, PDF.

---

## 6. Scorecards / modelos

Crivo tem editor de scorecards e importa modelos **PMML** (padrão de mercado).
Nós temos scorecard básico (regras com peso). Falta editor dedicado e PMML.

---

## 7. Quadro-resumo de gaps (priorização sugerida)

| # | Gap | Valor | Esforço | Observação |
|---|---|---|---|---|
| 1 | **Menu do editor no estilo Crivo** | Alto | Baixo | Reorganizar seções (frontend) |
| 2 | **Ações: contador Limite + Justificativa** | Alto | Médio | Enriquece a decisão |
| 3 | **Parâmetros de saída nomeados** | Alto | Médio | Devolver limite/taxa/justificativa |
| 4 | **Regra Matriz** (cruzamento de faixas) | Alto | Médio | Diferencial do Crivo |
| 5 | **Variáveis Globais** | Médio | Médio | Reuso entre políticas |
| 6 | **Comentário no fluxo** | Baixo | Baixo | Anotação visual |
| 7 | **Objetos de Análise** (sub-análises) | Alto | Alto | Encadear PF/PJ |
| 8 | **Campeão × Desafiante** (A/B) | Médio | Alto | Testes controlados |
| 9 | **Operação em lote + relatórios/BI** | Médio | Alto | Operação/auditoria |
| 10 | **Scorecard dedicado + PMML** | Médio | Alto | Se usar modelos estatísticos |
| 11 | **Modelo lista-de-regras** (vs. grafo) | — | Alto | Decisão estrutural; avaliar |

---

## 8. Recomendação de ordem

1. **Menu do editor no estilo Crivo** (baixo esforço, alinha a experiência).
2. **Ações + contador Limite + Justificativa** e **Parâmetros de saída nomeados**
   (enriquecem a decisão de forma tangível).
3. **Regra Matriz** (diferencial claro).
4. **Variáveis Globais** e **Comentário**.
5. Depois avaliar os grandes: **Objetos de Análise**, **Campeão × Desafiante**,
   **lote/relatórios**, **PMML** e a eventual mudança para **modelo de lista de
   regras**.

> A mudança para "lista sequencial de regras" (item 11) é a mais impactante e
> conceitual; recomendo decidir sobre ela explicitamente antes de investir, pois
> altera bastante o editor atual (canvas/grafo).
