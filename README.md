# usebens-motor-decisao

Motor de decisão de crédito no estilo do **Crivo** (TransUnion): orquestração de
fluxos de decisão desenhados visualmente (drag-and-drop), motor de regras e de
fórmulas no estilo Excel, scorecards e trilha de auditoria por decisão.

Este repositório contém o **backend em C# (.NET Core / .NET 10)** e o front-end.

## Início rápido

Comandos mínimos para subir a aplicação em desenvolvimento. Detalhes e variações
estão nas seções mais abaixo.

Pré-requisitos: **.NET SDK 10**, **Node ≥ 22.22.3** e acesso ao **AWS SSO**.

> Os `export PATH` abaixo são condicionais: só ajustam o PATH se o `dotnet`/`node`
> estiverem instalados em `~/.dotnet` / `~/.node/bin` (caso de uma das máquinas de
> dev). Se você instalou via Homebrew (`dotnet`/`node` já no PATH), essas linhas
> não têm efeito e podem ser ignoradas.

**1) Backend (.NET) contra o RDS compartilhado** — o token SSO expira; se o
startup falhar por credencial, rode `aws sso login` e suba de novo. A API sobe em
`http://localhost:5080` (valide com `/flows`, `/sources`, `/global-variables`).

```bash
[ -d "$HOME/.dotnet" ] && export PATH="$HOME/.dotnet:$PATH"
unset DATABASE__HOST DATABASE__PORT DATABASE__NAME DATABASE__USER DATABASE__PASSWORD DATABASE__CONNECTIONSTRING
export USE_SECRETS_MANAGER=true AWS_DEFAULT_REGION=us-east-1 \
  POSTGRES_SECRET_NAME=databases/postgres-motor-decisao \
  ASPNETCORE_URLS=http://localhost:5080 DOTNET_ENVIRONMENT=Production
dotnet run --project src/MotorDecisao.Api/MotorDecisao.Api.csproj --no-launch-profile
```

**2) Front-end Angular (destino da migração)** — em outro terminal. Sobe em
`http://localhost:4200` com proxy `/api` para `:5080`. Na primeira vez (ou após
mudar dependências), rode `npm install` antes do `ng serve`.

```bash
[ -d "$HOME/.node/bin" ] && export PATH="$HOME/.node/bin:$PATH"
export CI=true NG_CLI_ANALYTICS=false
cd frontend-angular
npm install
npx ng serve --port 4200
```

**2b) Front-end React (alternativo, ainda mantido)** — em outro terminal. Sobe em
`http://localhost:5173` com proxy `/api` para `:5080`. Rode `npm install` na
primeira vez.

```bash
[ -d "$HOME/.node/bin" ] && export PATH="$HOME/.node/bin:$PATH"
export VITE_API_TARGET=http://localhost:5080
cd frontend
npm install
npm run dev -- --host
```

## Stack

| Camada | Tecnologia | Situação |
|---|---|---|
| Front-end | **Angular + Angular Material** em `frontend-angular/` | em migração (React + React Flow em `frontend/` ainda coexiste) |
| Back-end | **.NET Core (.NET 10)**, clean architecture | ativo |
| Banco relacional | **PostgreSQL** (RDS compartilhado, schema `usebens_motor_decisao`) | ativo |
| Cache | **Redis** | implementado (opt-in via `REDIS__ENABLED`; fallback in-memory) |
| Banco não-relacional | **MongoDB** | somente quando houver necessidade real |
| Mensageria / filas | **RabbitMQ** | somente quando houver necessidade real |

> Mongo e RabbitMQ não são adicionados ao código enquanto não houver um caso de
> uso concreto (evitar infra órfã). O `docker-compose.dev.yml` já os traz
> definidos, porém desligados por padrão (ative por `--profile` quando precisar).

## Arquitetura

Clean architecture, uma solução com quatro projetos:

| Projeto | Responsabilidade |
|---|---|
| `MotorDecisao.Domain` | Entidades e enums do domínio. Sem dependências externas. |
| `MotorDecisao.Application` | Casos de uso e serviços de aplicação (a preencher). |
| `MotorDecisao.Infrastructure` | EF Core + PostgreSQL, `DbContext`, migrations, configuração. |
| `MotorDecisao.Api` | Host ASP.NET Core: endpoints HTTP, health checks, DI. |

### Modelo de domínio (resumo)

- **DecisionFlow** → política nomeada; contém várias **FlowVersion**.
- **FlowVersion** → snapshot versionado (draft / published / archived). Guarda o
  grafo do editor: **FlowNode** (blocos) e **FlowEdge** (conexões), além de
  **Ruleset**, **Rule** e **Formula**.
- **Rule** → condição no estilo Excel + efeito (pontua o scorecard, força um
  desfecho, ou apenas anota).
- **DecisionExecution** + **ExecutionTrace** → cada execução do motor para uma
  proposta, com entrada, desfecho e passo a passo auditável.

Todas as tabelas vivem no schema dedicado **`usebens_motor_decisao`**, no mesmo
banco PostgreSQL compartilhado pelos demais projetos (ex.: OKR).

## Pré-requisitos

- .NET SDK **10.0**
- PostgreSQL (banco compartilhado; este serviço usa o schema `usebens_motor_decisao`)

## Configuração

As configurações seguem o mesmo padrão dos outros serviços:

- **Produção (EKS)**: `USE_SECRETS_MANAGER=true` (padrão). As credenciais são
  lidas do **AWS Secrets Manager** via IRSA (IAM Role for Service Account),
  usando a cadeia de credenciais padrão do SDK da AWS.
- **Local**: `USE_SECRETS_MANAGER=false` para usar as variáveis `DATABASE__*` do
  `.env` (sem exigir acesso à AWS). Veja `.env.example`.

Chaves relevantes (o `__` mapeia para seções aninhadas de configuração):

```
USE_SECRETS_MANAGER          # true = produção (Secrets Manager); false = .env local
AWS_DEFAULT_REGION           # região do secret (default us-east-1)
POSTGRES_SECRET_NAME         # nome do secret (default databases/postgres-motor-decisao)

# usados apenas quando USE_SECRETS_MANAGER=false:
DATABASE__CONNECTIONSTRING   # string completa (tem precedência quando definida)
DATABASE__HOST / __PORT / __NAME / __USER / __PASSWORD   # partes alternativas
```

O secret é um JSON no formato RDS que os demais serviços já usam
(`host`, `port`, `username`, `password`, `dbname`; o resolver também aceita
`login` para usuário e `database` para o banco). O schema é sempre
`usebens_motor_decisao`, independentemente do secret.

## Infra local (Docker)

As dependências de apoio para desenvolvimento sobem via `docker-compose.dev.yml`.
Hoje só o **Redis** está ativo por padrão; **MongoDB** e **RabbitMQ** ficam
desligados atrás de _profiles_ e só devem ser ligados quando houver necessidade.

> Requer Docker + plugin Compose. Se ainda não tiver:
> `sudo apt install docker.io docker-compose-v2` (ou `sudo snap install docker`),
> depois `sudo usermod -aG docker $USER` e reabra a sessão para usar sem `sudo`.

```bash
# a partir da raiz do projeto (usebens-motor-credito)

# subir só o Redis (padrão)
docker compose -f docker-compose.dev.yml up -d

# status e logs
docker compose -f docker-compose.dev.yml ps
docker compose -f docker-compose.dev.yml logs -f

# ligar Mongo / RabbitMQ quando (e se) forem necessários
docker compose -f docker-compose.dev.yml --profile mongo up -d
docker compose -f docker-compose.dev.yml --profile rabbitmq up -d

# parar (mantém os dados nos volumes) / parar e apagar os dados
docker compose -f docker-compose.dev.yml down
docker compose -f docker-compose.dev.yml down -v
```

Portas e credenciais de desenvolvimento (definidas no compose):

| Serviço | Porta | Usuário | Senha | Observação |
|---|---|---|---|---|
| Redis | `6379` | — | `motor` | conecta com `requirepass` |
| MongoDB | `27017` | `motor` | `motor` | db inicial `motor_decisao` (profile `mongo`) |
| RabbitMQ | `5672` (AMQP) / `15672` (console) | `motor` | `motor` | console em http://localhost:15672 (profile `rabbitmq`) |

## Rodando localmente

```bash
# a partir da raiz do repositório
dotnet build MotorDecisao.slnx

# aplicar as migrations no banco (cria o schema usebens_motor_decisao)
dotnet dotnet-ef database update \
  --project src/MotorDecisao.Infrastructure/MotorDecisao.Infrastructure.csproj \
  --startup-project src/MotorDecisao.Api/MotorDecisao.Api.csproj

# subir a API
dotnet run --project src/MotorDecisao.Api/MotorDecisao.Api.csproj
```

> As migrations já foram aplicadas ao **RDS compartilhado** (schema
> `usebens_motor_decisao`). Para subir a API contra o RDS real, veja o comando
> completo em "Notas para o agente". O `database update` acima é para aplicar
> migrations pendentes ou preparar outro banco.

### Endpoints

Serviço / saúde:

| Método | Rota | Descrição |
|---|---|---|
| GET | `/` | Identidade do serviço (`{ service, status }`). |
| GET | `/health/live` | Liveness (processo no ar). |
| GET | `/health/ready` | Readiness (processo + banco acessível). |
| GET | `/openapi/v1.json` | Documento OpenAPI (apenas em Development). |

Fluxos e versões (autoria no editor):

| Método | Rota | Descrição |
|---|---|---|
| POST | `/flows` | Cria um fluxo (já cria a versão 1 em rascunho). |
| GET | `/flows` | Lista os fluxos. |
| GET | `/flows/{id}` | Detalhe do fluxo + versões. |
| POST | `/flows/{id}/versions` | Cria nova versão (opcionalmente copiando de outra). |
| GET | `/flows/{id}/versions/{versionId}` | Carrega o grafo da versão (nós/arestas/regras/fórmulas). |
| PUT | `/flows/{id}/versions/{versionId}` | Salva o grafo desenhado (somente versões em rascunho). |
| POST | `/flows/{id}/versions/{versionId}/publish` | Valida e publica a versão (arquiva a anterior, invalida o cache). |

Decisão e auditoria:

| Método | Rota | Descrição |
|---|---|---|
| POST | `/flows/{id}/decisions` | Executa uma decisão contra a versão publicada. |
| GET | `/flows/{id}/executions` | Lista execuções recentes do fluxo. |
| GET | `/executions/{id}` | Detalhe de uma execução com a trilha completa. |

> A publicação valida o grafo (um único nó inicial + fórmulas válidas). Se algo
> não compilar, retorna `400` com a mensagem do problema em português.
> Autenticação (JWT) ainda não está habilitada — é um ponto de extensão.

## Migrations (EF Core)

```bash
# criar uma nova migration
dotnet dotnet-ef migrations add <Nome> \
  --project src/MotorDecisao.Infrastructure/MotorDecisao.Infrastructure.csproj \
  --startup-project src/MotorDecisao.Api/MotorDecisao.Api.csproj \
  --output-dir Persistence/Migrations

# gerar script SQL idempotente (para revisão / aplicação manual)
dotnet dotnet-ef migrations script --idempotent \
  --project src/MotorDecisao.Infrastructure/MotorDecisao.Infrastructure.csproj \
  --startup-project src/MotorDecisao.Api/MotorDecisao.Api.csproj \
  --output motor_migrations.sql
```

## Docker

```bash
docker build -t usebens-motor-decisao .
docker run --rm -p 8080:8080 --env-file .env usebens-motor-decisao
```

A imagem expõe a porta `8080` e traz um `HEALTHCHECK` em `/health/live`.

## Front-end

Há dois front-ends durante a migração; ambos consomem a mesma API.

- **Angular + Angular Material** em [`frontend-angular/`](frontend-angular/) — o
  destino da migração. Já cobre Políticas (lista/detalhe/histórico), Fontes,
  Variáveis Globais e Execuções (trilha por blocos + árvore de resolução), além
  de uma 1ª iteração do editor de grafo (`@foblex/flow`).
- **React + React Flow** em [`frontend/`](frontend/README.md) — o front original,
  mantido até o Angular alcançar paridade total no editor.

Rodar o Angular (dev):

```bash
cd frontend-angular
export PATH="$HOME/.node/bin:$PATH"
export CI=true NG_CLI_ANALYTICS=false   # evita o prompt interativo de autocompletion
npx ng serve --port 4200                # proxy /api -> http://localhost:5080
# build de produção:
npx ng build
```

> Requer Node ≥ 22.22.3 (Angular CLI mais recente). O dev server usa
> `proxy.conf.json` para encaminhar `/api` ao backend em `:5080`, o mesmo
> contrato do front React.

### Estado da migração para Angular

O Angular já tem **paridade funcional** com o React (build limpo + dados reais
via proxy), incluindo:

- Shell (toolbar + sidenav Material), camada de API tipada, e o **visual
  refinado** (tema da marca, cabeçalhos de página, cards, estados vazios/erro).
- Políticas (lista/detalhe/histórico), Fontes, Variáveis Globais e Execuções
  (trilha por blocos + árvore de resolução expansível + downloads).
- Editor de grafo (`@foblex/flow`): carregar/salvar/publicar, arrastar nós,
  conectar, configurar por duplo-clique, somente-leitura ao publicar.
- **Editores visuais** de Regra Matriz (faixas + grade de células) e Conjunto de
  regras; **modal de destino Verdadeiro/Falso** da condição; botão **Testar**
  (modal de decisão); e painéis de **Minhas Variáveis** e **Campos** no editor.

Refinos possíveis (não bloqueiam o uso): autocomplete de fórmula (campos/variáveis/
fontes) nos editores do Angular como no React, e polimento visual do canvas.

## Roadmap

Concluído:

1. ✅ Fundação (backend + persistência PostgreSQL + cache em memória).
2. ✅ Engine de fórmulas no estilo Excel em C# (parser + avaliador, funções pt-BR).
3. ✅ Engine de execução do fluxo (percorre o grafo e produz desfecho + trilha).
4. ✅ API de CRUD dos fluxos + publicação + decisão + auditoria.
5. ✅ Front-end do editor visual drag-and-drop (React + React Flow).

Em andamento / próximos:

- 🚧 Migração do front-end para **Angular + Angular Material** (`frontend-angular/`):
  telas principais e a 1ª iteração do editor prontas; falta paridade fina do
  editor (Matrix/Ruleset visuais, modal V/F, Testar) antes de aposentar o React.
- ✅ Cache de fluxos publicados em **Redis** (opt-in `REDIS__ENABLED`, fallback
  in-memory; resolve o cenário multi-réplica antes contornado por _stamp_).
- 🔜 Integração real de fontes de dados externas (bureaus/APIs) via
  `IDataSourceResolver` (hoje há fakes: SERASA e BACEN).
- 🔜 Autenticação (JWT) e observabilidade.
- ⏳ **MongoDB** e **RabbitMQ**: adotar somente quando houver caso de uso concreto
  (ex.: trilhas de execução em Mongo; decisão assíncrona/lote em RabbitMQ).

---

## Notas para o agente (contexto operacional)

Seção para quem for retomar este projeto com um agente de IA. Resume o ambiente,
os atalhos e as convenções que não são óbvios pelo código.

### Ambiente da máquina de dev

- `dotnet` **não está no PATH** por padrão: use
  `export PATH="$HOME/.dotnet:$PATH"` (SDK 10.0.x).
- `node`/`npm` **não estão no PATH** por padrão: use
  `export PATH="$HOME/.node/bin:$PATH"` (Node 22.x, npm 10.x).
- O arquivo de solução é **`MotorDecisao.slnx`** (formato novo, não `.sln`).
- Binários do PostgreSQL client (se precisar): `/usr/lib/postgresql/16/bin`.
- **Docker não vem instalado** — veja "Infra local (Docker)" para instalar.

### Banco de dados (RDS real compartilhado)

- O projeto usa o **RDS compartilhado** no schema `usebens_motor_decisao` (não há
  Postgres local no compose de propósito).
- As credenciais vêm do **AWS Secrets Manager** (secret
  `databases/postgres-motor-decisao`), resolvidas via AWS SSO.
- O token SSO **expira** e derruba a subida da API (erro de credencial no
  startup, em `PostgresConnectionResolver`). Quando isso acontecer:
  `aws sso login` e suba de novo.
- O secret guarda `port` como string; há um `FlexibleIntConverter` que lida com
  isso. O schema é sempre `usebens_motor_decisao`, independentemente do secret.

### Subir a API contra o RDS real (comando usado nas sessões)

```bash
export PATH="$HOME/.dotnet:$PATH"
unset DATABASE__HOST DATABASE__PORT DATABASE__NAME DATABASE__USER DATABASE__PASSWORD DATABASE__CONNECTIONSTRING
export USE_SECRETS_MANAGER=true AWS_DEFAULT_REGION=us-east-1 \
  POSTGRES_SECRET_NAME=databases/postgres-motor-decisao \
  ASPNETCORE_URLS=http://localhost:5080 DOTNET_ENVIRONMENT=Production
dotnet run --project src/MotorDecisao.Api/MotorDecisao.Api.csproj --no-launch-profile
```

> A API sobe em `http://localhost:5080`. **Não** existe rota `/health` nesse host
> de dev — valide com rotas reais (`/flows`, `/sources`, `/global-variables`).
> Se a porta 5080 estiver presa por um processo antigo
> (`ss -ltnp | grep 5080`), mate o PID órfão antes de subir de novo.

### Cache (Redis) e fallback

- O cache dos fluxos publicados tem duas implementações de `IPublishedFlowCache`:
  `InMemoryPublishedFlowCache` (padrão) e `RedisPublishedFlowCache`.
- A escolha é por env: `REDIS__ENABLED=true` liga o Redis; qualquer outra coisa
  mantém o in-memory. Config em `REDIS__CONNECTIONSTRING` **ou**
  `REDIS__HOST`/`REDIS__PORT`/`REDIS__PASSWORD` (batem com o `docker-compose.dev.yml`:
  `localhost:6379`, senha `motor`).
- Resiliência: a connection string usa `abortConnect=false`; se o Redis cair, as
  operações degradam para um _load_ direto do banco (não derruba a API).
- No Redis, as chaves ficam sob o prefixo `motor:published-flow:` — uma string por
  fluxo (com TTL) mais um SET `...:index` que o `InvalidateAll()` usa para limpar
  tudo (ex.: quando uma variável global muda).
- Subir a API **com Redis**: adicione ao comando de subida
  `REDIS__ENABLED=true REDIS__HOST=localhost REDIS__PORT=6379 REDIS__PASSWORD=motor`
  (e tenha o container `motor-redis` de pé — veja "Infra local (Docker)").
- Inspecionar as chaves: `docker exec motor-redis redis-cli -a motor KEYS "motor:*"`.

### Front-end (React atual)

```bash
export PATH="$HOME/.node/bin:$PATH"
export VITE_API_TARGET=http://localhost:5080
npm run dev -- --host        # dev server (proxy /api -> 5080), porta 5173
npm run build                # tsc -b && vite build (checagem de tipos + build)
```

### Front-end (Angular — destino da migração, `frontend-angular/`)

```bash
export PATH="$HOME/.node/bin:$PATH"
export CI=true NG_CLI_ANALYTICS=false   # o ng serve TRAVA num prompt de autocompletion sem isto
npx ng serve --port 4200                # proxy /api -> 5080
npx ng build                            # build (checagem de tipos AOT)
```

- Node **≥ 22.22.3** é obrigatório para o Angular CLI mais recente. Nesta máquina
  o Node vive em `~/.node` (tarball); foi atualizado para 22.23.3 (backup em
  `~/.node.bak-*`).
- Angular 22 é **zoneless** (sem zone.js). `provideFFlow()` (do `@foblex/flow`)
  entra no `app.config.ts` — por isso o bundle inicial é maior (~900kB).
- A API do `@foblex/flow` foi verificada direto no bundle (inputs como
  `fNodePosition`, `fData`, eventos `fCreateNode`/`fCreateConnection`); não assuma
  nomes — confirme em `node_modules/@foblex/flow/fesm2022/foblex-flow.mjs`.

### Migrations (EF Core) — com credenciais do RDS

```bash
export PATH="$HOME/.dotnet:$PATH"
export USE_SECRETS_MANAGER=true AWS_DEFAULT_REGION=us-east-1 \
  POSTGRES_SECRET_NAME=databases/postgres-motor-decisao DOTNET_ENVIRONMENT=Production
dotnet dotnet-ef migrations add <Nome> \
  --project src/MotorDecisao.Infrastructure/MotorDecisao.Infrastructure.csproj \
  --startup-project src/MotorDecisao.Api/MotorDecisao.Api.csproj
dotnet dotnet-ef database update \
  --project src/MotorDecisao.Infrastructure/MotorDecisao.Infrastructure.csproj \
  --startup-project src/MotorDecisao.Api/MotorDecisao.Api.csproj
```

`dotnet-ef` é uma ferramenta local (`.config/dotnet-tools.json`). A factory de
design-time (`MotorDecisaoDbContextFactory`) honra o Secrets Manager, então as
migrations rodam contra o RDS real com as mesmas variáveis da API.

### Testes

- `dotnet test MotorDecisao.slnx` — suíte completa (Application + Api).
- Os testes de API usam `WebApplicationFactory` com **EF InMemory**
  (`USE_SECRETS_MANAGER=false`), então **não** tocam o RDS.

### Convenções do projeto

- Código e nomes de domínio em **inglês**; UI e comentários em **pt-BR**.
- Linguagem de fórmula: `'campo'` (campo de entrada), `"texto"` (literal),
  `{variavel}` (variável, local ou global), `[Fonte;Produto;Dado]` (fonte
  externa). Separador de argumentos de função: `;`. Funções com nomes pt-BR
  (SE, SOMA, ARRED, RAIZ, RAIZCUBICA, TRUNCAR, …).
- **Fontes externas** implementam `IExternalSource` e se auto-registram por DI;
  o dado reservado `Disponibilidade` (booleano) existe em todo produto e é
  respondido pelo `SourceCatalog`. Hoje há fakes **SERASA** e **BACEN** (BACEN é
  determinístico por CPF).
- **Variáveis**: locais (por versão, entidade `Formula`) e **globais**
  (`GlobalVariable`, tabela `global_variables`, CRUD em `/global-variables`).
  Local de mesmo nome **tem prioridade** sobre a global (`VariableMerge`).
- Ao mexer em algo que afeta todos os fluxos (ex.: variável global), invalidar o
  cache com `InvalidateAll()`.

### Regras de ouro ao trabalhar aqui

- Rodar `dotnet build`/`dotnet test` e `npm run build` antes de dar por pronto.
- Ao validar e2e no RDS real, **criar dados de teste com prefixo claro**
  (ex.: `__ALGO_TEST__`) e **apagar ao final** — preservar as políticas do
  usuário (ex.: `RODOBENS` / `(CREDITO)_POLITICA_SCORE`).
- Migrations no RDS real são aditivas e de baixo risco quando só criam
  colunas/tabelas novas; ainda assim, revisar o arquivo gerado antes de aplicar.
