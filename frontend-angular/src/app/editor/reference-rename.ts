import type { FlowNodeKind, GraphFormula, GraphRuleset } from '../api/models';

/** O que está sendo renomeado — define a sintaxe de referência a reescrever. */
export type RenameKind = 'variable' | 'field' | 'table';

/** Escapa uma string para uso literal dentro de uma RegExp. */
function escapeRegExp(s: string): string {
  return s.replace(/[.*+?^${}()|[\]\\]/g, '\\$&');
}

/**
 * Reescreve as referências a um nome em UMA expressão. A sintaxe de cada tipo é
 * delimitada, o que torna a troca segura contra colisão de substring:
 * - variável: `{nome}` (chaves)
 * - campo:    `'nome'` (aspas simples)
 * - tabela:   1º argumento (entre aspas duplas) de PROCV/PROCV.FAIXA — ancorado
 *   na chamada da função para não colidir com literais de texto nem com o nome
 *   da coluna (2º argumento).
 */
export function rewriteExpression(
  expr: string,
  kind: RenameKind,
  oldName: string,
  newName: string,
): string {
  if (!expr || oldName === newName) return expr;
  const old = escapeRegExp(oldName);

  switch (kind) {
    case 'variable': {
      // {  oldName  } -> {newName} (tolera espaços internos, como o editor não insere).
      const re = new RegExp(`\\{\\s*${old}\\s*\\}`, 'g');
      return expr.replace(re, `{${newName}}`);
    }
    case 'field': {
      // 'oldName' -> 'newName'
      const re = new RegExp(`'\\s*${old}\\s*'`, 'g');
      return expr.replace(re, `'${newName}'`);
    }
    case 'table': {
      // PROCV( "oldName"  ou  PROCV.FAIXA( "oldName"  -> troca só o 1º argumento,
      // preservando os espaços/《abre-parêntese》 capturados.
      const re = new RegExp(`(PROCV(?:\\.FAIXA)?\\s*\\(\\s*)"\\s*${old}\\s*"`, 'gi');
      return expr.replace(re, `$1"${newName}"`);
    }
    default:
      return expr;
  }
}

/**
 * Reescreve todas as expressões dentro de um config de nó (string JSON),
 * conforme o tipo do nó. Espelha os campos montados pelo node-config-dialog.
 * Retorna o JSON reserializado; em erro de parse, devolve o config original.
 */
export function rewriteNodeConfig(
  configJson: string,
  kind: FlowNodeKind,
  renameKind: RenameKind,
  oldName: string,
  newName: string,
): string {
  if (!configJson) return configJson;
  let cfg: Record<string, unknown>;
  try {
    cfg = JSON.parse(configJson);
  } catch {
    return configJson; // config inválido: não mexe
  }

  const rw = (s: unknown): string =>
    typeof s === 'string' ? rewriteExpression(s, renameKind, oldName, newName) : (s as string);

  // Reescreve a lista de ações (usada por vários tipos de nó).
  const rewriteActions = (actions: unknown): void => {
    if (!Array.isArray(actions)) return;
    for (const a of actions) {
      if (a && typeof a === 'object' && 'expression' in a) {
        (a as { expression: unknown }).expression = rw((a as { expression: unknown }).expression);
      }
    }
  };

  switch (kind) {
    case 'Condition':
      cfg['expression'] = rw(cfg['expression']);
      rewriteActions(cfg['trueActions']);
      rewriteActions(cfg['falseActions']);
      break;
    case 'Computation':
      if (Array.isArray(cfg['assignments'])) {
        for (const a of cfg['assignments'] as Array<Record<string, unknown>>) {
          a['expression'] = rw(a['expression']);
        }
      }
      rewriteActions(cfg['actions']);
      break;
    case 'Decision':
    case 'DataSource':
    case 'Action':
      rewriteActions(cfg['actions']);
      break;
    case 'Matrix':
      cfg['rowExpression'] = rw(cfg['rowExpression']);
      cfg['colExpression'] = rw(cfg['colExpression']);
      // Células e valor padrão podem conter expressões (modos Pontos/Limite).
      if (Array.isArray(cfg['cells'])) {
        cfg['cells'] = (cfg['cells'] as unknown[][]).map((row) =>
          Array.isArray(row) ? row.map((c) => rw(c)) : row,
        );
      }
      cfg['defaultValue'] = rw(cfg['defaultValue']);
      break;
    // Comment e demais: sem expressões.
  }

  return JSON.stringify(cfg);
}

/** Reescreve as expressões das variáveis (o campo `expression`). */
export function rewriteFormulas(
  formulas: GraphFormula[],
  kind: RenameKind,
  oldName: string,
  newName: string,
): GraphFormula[] {
  return formulas.map((f) => ({ ...f, expression: rewriteExpression(f.expression, kind, oldName, newName) }));
}

/** Reescreve as condições das regras de todos os conjuntos. */
export function rewriteRulesets(
  rulesets: GraphRuleset[],
  kind: RenameKind,
  oldName: string,
  newName: string,
): GraphRuleset[] {
  return rulesets.map((rs) => ({
    ...rs,
    rules: rs.rules.map((r) => ({
      ...r,
      conditionExpression: rewriteExpression(r.conditionExpression, kind, oldName, newName),
    })),
  }));
}
