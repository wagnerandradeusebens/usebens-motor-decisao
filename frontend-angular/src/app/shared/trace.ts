import type { ExecutionDetail, TraceCategory, TraceStep } from '../api/models';
import { fmtDate } from './format';

/** Rótulo legível de cada bloco da trilha, na ordem de exibição. */
export const CATEGORY_ORDER: TraceCategory[] = [
  'Inicio', 'Fonte', 'Variavel', 'Regra', 'Acao', 'Matriz', 'Fluxo', 'Decisao',
];

export const CATEGORY_LABEL: Record<TraceCategory, string> = {
  Inicio: 'Início',
  Fonte: 'Fontes',
  Variavel: 'Variáveis',
  Regra: 'Regras',
  Acao: 'Ações',
  Matriz: 'Matrizes',
  Fluxo: 'Fluxo',
  Decisao: 'Decisão',
};

export interface TraceGroup {
  category: TraceCategory;
  label: string;
  steps: TraceStep[];
}

/** Uma política na trilha, com seus passos agrupados por categoria. */
export interface PolicyTraceGroup {
  /** Nome da política; null/'' para passos sem política (traces antigos). */
  policyName: string | null;
  /** Rótulo exibido (o nome, ou "Política principal" quando não há nome). */
  label: string;
  categories: TraceGroup[];
}

/** Agrupa os passos da trilha por categoria, preservando a ordem original. */
export function groupByCategory(trace: TraceStep[]): TraceGroup[] {
  const buckets = new Map<TraceCategory, TraceStep[]>();
  for (const step of trace) {
    const cat = step.category ?? 'Fluxo';
    if (!buckets.has(cat)) buckets.set(cat, []);
    buckets.get(cat)!.push(step);
  }
  const ordered: TraceGroup[] = CATEGORY_ORDER.filter((c) => buckets.has(c)).map((c) => ({
    category: c,
    label: CATEGORY_LABEL[c],
    steps: buckets.get(c)!,
  }));
  // Categorias inesperadas ainda aparecem no fim.
  for (const [cat, steps] of buckets) {
    if (!CATEGORY_ORDER.includes(cat)) {
      ordered.push({ category: cat, label: cat, steps });
    }
  }
  return ordered;
}

/**
 * Agrupa a trilha por POLÍTICA (na ordem em que cada política aparece pela 1ª
 * vez) e, dentro de cada política, por categoria (na ordem canônica). Isso
 * separa a execução da principal e das subpolíticas e evita misturar tudo num
 * balde só. A ordem de execução é preservada dentro de cada categoria.
 */
export function groupByPolicy(trace: TraceStep[]): PolicyTraceGroup[] {
  // Preserva a ordem de 1ª aparição de cada política.
  const order: string[] = [];
  const buckets = new Map<string, TraceStep[]>();
  for (const step of trace) {
    const key = step.policyName ?? '';
    if (!buckets.has(key)) {
      buckets.set(key, []);
      order.push(key);
    }
    buckets.get(key)!.push(step);
  }
  return order.map((key) => ({
    policyName: key || null,
    label: key || 'Política principal',
    categories: groupByCategory(buckets.get(key)!),
  }));
}

/** Um dado coletado de uma fonte: nome, valor e origem (online/cache). */
export interface SourceDatumReport {
  datum: string;
  value: string;
  online: boolean;
}

/** Bloco do "Relatório das Fontes": uma fonte/produto e seus dados coletados. */
export interface SourceReportGroup {
  source: string;
  product: string;
  data: SourceDatumReport[];
}

/**
 * Monta o "Relatório das Fontes" (estilo Crivo) a partir da trilha: pega os
 * passos de categoria Fonte, lê a referência `[Fonte;Produto;Dado]` da expressão
 * e agrupa por Fonte → Produto, listando cada dado com seu valor e se veio de
 * consulta online ou do cache. A ordem de aparição é preservada.
 */
export function sourcesReport(trace: TraceStep[]): SourceReportGroup[] {
  const order: string[] = [];
  const map = new Map<string, SourceReportGroup>();

  for (const step of trace) {
    if (step.category !== 'Fonte') continue;
    const ref = parseSourceRef(step.expression);
    if (!ref) continue;
    // O dado "Disponibilidade" é status, não faz parte do laudo de dados.
    const key = `${ref.source}\u0001${ref.product}`;
    let group = map.get(key);
    if (!group) {
      group = { source: ref.source, product: ref.product, data: [] };
      map.set(key, group);
      order.push(key);
    }
    group.data.push({
      datum: ref.datum,
      value: step.result ?? '',
      online: (step.sourceOrigin ?? 'Online') !== 'Cache',
    });
  }

  return order.map((k) => map.get(k)!);
}

/** Extrai (Fonte, Produto, Dado) de uma expressão `[Fonte;Produto;Dado]`. */
function parseSourceRef(expr: string | null): { source: string; product: string; datum: string } | null {
  if (!expr) return null;
  const m = /^\[([^;]+);([^;]+);([^\]]+)\]$/.exec(expr.trim());
  if (!m) return null;
  return { source: m[1].trim(), product: m[2].trim(), datum: m[3].trim() };
}

/** Monta um log textual legível de uma execução (para download .txt). */
export function toTextLog(d: ExecutionDetail): string {
  const lines: string[] = [];
  lines.push(`Execução: ${d.id}`);
  lines.push(`Referência: ${d.proposalReference ?? '—'}`);
  lines.push(`Desfecho: ${d.outcome}  |  Pontos: ${d.score}  |  Limite: ${d.limit}  |  Status: ${d.status}`);
  lines.push(`Criada: ${fmtDate(d.createdAt)}  |  Concluída: ${fmtDate(d.completedAt)}`);
  if (d.error) lines.push(`Erro: ${d.error}`);
  lines.push('');
  lines.push('Entrada:');
  lines.push(pretty(d.inputData));
  if (d.justifications.length) {
    lines.push('');
    lines.push('Justificativas:');
    d.justifications.forEach((j) => lines.push(`  - ${j}`));
  }
  if (Object.keys(d.outputs).length) {
    lines.push('');
    lines.push('Parâmetros de saída:');
    Object.entries(d.outputs).forEach(([k, v]) => lines.push(`  ${k} = ${v}`));
  }
  lines.push('');
  lines.push('Log passo a passo (por política e bloco):');
  groupByPolicy(d.trace).forEach((policy) => {
    lines.push('');
    lines.push(`######## ${policy.label} ########`);
    policy.categories.forEach(({ label, steps }) => {
      lines.push('');
      lines.push(`  == ${label} ==`);
      steps.forEach((t) => {
        lines.push(`    #${t.sequence} [${t.nodeLabel}]${t.expression ? ` ${t.expression}` : ''}`);
        if (t.result) lines.push(`        resultado: ${t.result}`);
        if (t.message) lines.push(`        ${t.message}`);
        if (t.detail?.length) {
          lines.push('        resolução:');
          t.detail.forEach((s) => {
            lines.push(`          ${'  '.repeat(s.depth)}${s.expression} = ${s.value}`);
          });
        }
      });
    });
  });
  return lines.join('\n');
}

/** Formata um JSON bonito, caindo para o valor cru se não for JSON. */
export function pretty(json: string): string {
  try {
    return JSON.stringify(JSON.parse(json), null, 2);
  } catch {
    return json;
  }
}

/** Dispara o download de um conteúdo no navegador. */
export function download(filename: string, content: string, mime: string): void {
  const blob = new Blob([content], { type: mime });
  const url = URL.createObjectURL(blob);
  const a = document.createElement('a');
  a.href = url;
  a.download = filename;
  a.click();
  URL.revokeObjectURL(url);
}
