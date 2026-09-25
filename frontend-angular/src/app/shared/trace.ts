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
  lines.push('Log passo a passo (por bloco):');
  groupByCategory(d.trace).forEach(({ label, steps }) => {
    lines.push('');
    lines.push(`== ${label} ==`);
    steps.forEach((t) => {
      lines.push(`  #${t.sequence} [${t.nodeLabel}]${t.expression ? ` ${t.expression}` : ''}`);
      if (t.result) lines.push(`      resultado: ${t.result}`);
      if (t.message) lines.push(`      ${t.message}`);
      if (t.detail?.length) {
        lines.push('      resolução:');
        t.detail.forEach((s) => {
          lines.push(`        ${'  '.repeat(s.depth)}${s.expression} = ${s.value}`);
        });
      }
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
