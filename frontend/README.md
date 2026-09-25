# usebens-motor-decisao · frontend

Editor visual do motor de decisão (estilo Crivo): desenho de fluxos
drag-and-drop, edição de regras e fórmulas no estilo Excel (pt-BR), publicação de
versões e um executor de decisões de teste. Consome a API .NET deste repositório.

## Stack

- **Vite + React + TypeScript**
- **React Flow** para o canvas de nós e conexões
- **React Router** para navegação

## Rodando localmente

Requer Node 20+.

```bash
cd frontend
npm install
npm run dev        # sobe em http://localhost:5173
```

O dev server faz proxy de `/api` para o backend. Ajuste o alvo em `.env`
(`VITE_API_TARGET`, padrão `http://localhost:5080`). Suba a API antes:

```bash
# na raiz do repositório
dotnet run --project src/MotorDecisao.Api/MotorDecisao.Api.csproj
```

## Build de produção

```bash
npm run build      # tsc + vite build -> dist/
npm run preview    # serve o build localmente
```

Em produção, aponte o cliente para a origem da API publicada via
`VITE_API_BASE` (senão usa o proxy `/api` do dev server).

## Estrutura

```
src/
  api/            cliente tipado + tipos espelhando os contratos do backend
  components/     shell da aplicação
  pages/          lista de fluxos, detalhe do fluxo, editor
  editor/         canvas (React Flow), tipos de nó, inspetor, regras, teste
  router.tsx      rotas
  styles.css      tema
```

## Fluxo de uso

1. **Criar fluxo** na tela inicial (cria a versão 1 em rascunho).
2. **Editar a versão**: arraste blocos (Start, Condition, Ruleset, Computation,
   DataSource, Decision) para o canvas, conecte-os e configure cada nó no painel
   à direita. Condições têm saídas verde (verdadeiro) e vermelha (falso).
3. **Regras**: na aba "Regras", monte os conjuntos (scorecards) e suas regras
   (pontuação, decisão forçada, anotação).
4. **Salvar** o rascunho e **Publicar** — a publicação valida o grafo no backend
   (um único nó inicial + fórmulas válidas); erros aparecem em português.
5. **Testar**: na aba "Testar" (disponível após publicar), informe os campos da
   proposta e execute a decisão para ver o desfecho, o score e a trilha completa.
```
