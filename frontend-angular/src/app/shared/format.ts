/** Formata uma data ISO para pt-BR, ou um travessão quando ausente. */
export function fmtDate(date: string | null | undefined): string {
  return date ? new Date(date).toLocaleString('pt-BR') : '—';
}

/** Extrai a mensagem de erro pt-BR do backend a partir de um HttpErrorResponse. */
export function apiErrorMessage(err: unknown, fallback: string): string {
  const e = err as { error?: { error?: string }; message?: string };
  if (e?.error && typeof e.error.error === 'string') {
    return e.error.error;
  }
  return fallback;
}
