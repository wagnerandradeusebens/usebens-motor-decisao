import { useMemo, useRef, useState } from 'react';
import type { SourceDescriptorDto } from '../api/types';
import { FUNCTIONS } from './functionCatalog';

interface Suggestion {
  /** Text inserted at the caret, replacing the current token. */
  insert: string;
  label: string;
  hint: string;
  /**
   * When set, after inserting, the [start,end] range (relative to the start of
   * the inserted text) is selected so the user types over the first placeholder.
   */
  selectWithin?: [number, number];
  /** Keep the dropdown open after inserting (for multi-part external refs). */
  keepOpen?: boolean;
}

interface Props {
  value: string;
  onChange: (value: string) => void;
  fields: string[]; // declared request fields (referenced with 'campo')
  variables: string[]; // variables (referenced with {variavel})
  sources: SourceDescriptorDto[]; // external sources (referenced with [Fonte;Produto;Dado])
  placeholder?: string;
  rows?: number;
  disabled?: boolean;
}

/**
 * Lightweight formula editor with autocomplete over the pt-BR language:
 * - typing '  → lists request fields (inserts 'campo')
 * - typing {  → lists variables (inserts {variavel})
 * - typing [  → walks Fonte → Produto → Dado from the source catalog
 * - typing a letter → lists functions (inserted with placeholder args, first
 *   placeholder selected) and known field/variable names as a convenience.
 * No heavy code editor dependency — plain React over a textarea.
 */
export function FormulaInput({ value, onChange, fields, variables, sources, placeholder, rows = 3, disabled }: Props) {
  const ref = useRef<HTMLTextAreaElement | null>(null);
  const [open, setOpen] = useState(false);
  const [caret, setCaret] = useState(0);

  const prefix = value.slice(0, caret);

  // Which "mode" the caret is in, based on the nearest unmatched trigger char.
  const mode = useMemo(() => detectMode(prefix), [prefix]);

  const suggestions = useMemo<Suggestion[]>(() => {
    switch (mode.kind) {
      case 'external': {
        const parts = mode.term.split(';');
        if (parts.length === 1) {
          const t = parts[0].trim().toLowerCase();
          return sources
            .filter((s) => s.name.toLowerCase().includes(t))
            .map((s) => ({ insert: s.name + ';', label: s.name, hint: 'fonte', keepOpen: true }));
        }
        if (parts.length === 2) {
          const src = sources.find((s) => s.name.toLowerCase() === parts[0].trim().toLowerCase());
          const t = parts[1].trim().toLowerCase();
          return (src?.products ?? [])
            .filter((p) => p.name.toLowerCase().includes(t))
            .map((p) => ({ insert: p.name + ';', label: p.name, hint: 'produto', keepOpen: true }));
        }
        if (parts.length === 3) {
          const src = sources.find((s) => s.name.toLowerCase() === parts[0].trim().toLowerCase());
          const prod = src?.products.find((p) => p.name.toLowerCase() === parts[1].trim().toLowerCase());
          const t = parts[2].trim().toLowerCase();
          return (prod?.data ?? [])
            .filter((d) => d.name.toLowerCase().includes(t))
            .map((d) => ({ insert: d.name + ']', label: d.name, hint: 'dado' }));
        }
        return [];
      }
      case 'field': {
        const t = mode.term.toLowerCase();
        return fields
          .filter((f) => f.toLowerCase().includes(t))
          .map((f) => ({ insert: f + "'", label: f, hint: 'campo' }));
      }
      case 'variable': {
        const t = mode.term.toLowerCase();
        return variables
          .filter((v) => v.toLowerCase().includes(t))
          .map((v) => ({ insert: v + '}', label: v, hint: 'variável' }));
      }
      case 'identifier': {
        const t = mode.term.toLowerCase();
        if (t.length < 1) return [];
        return FUNCTIONS.filter((f) => f.name.toLowerCase().startsWith(t)).map<Suggestion>((f) => {
          // Insert the function pre-formatted with placeholder args; select the
          // first placeholder so the user types over it.
          const inner = argsFromSignature(f.signature);
          const insert = `${f.name}(${inner})`;
          const firstArgLen = inner.split(';')[0]?.length ?? 0;
          const start = f.name.length + 1;
          return {
            insert,
            label: f.signature,
            hint: 'função',
            selectWithin: firstArgLen > 0 ? [start, start + firstArgLen] : undefined,
          };
        });
      }
      default:
        return [];
    }
  }, [mode, fields, variables, sources]);

  function applySuggestion(s: Suggestion) {
    const start = caret - mode.term.length;
    const next = value.slice(0, start) + s.insert + value.slice(caret);
    onChange(next);
    setOpen(Boolean(s.keepOpen));
    requestAnimationFrame(() => {
      ref.current?.focus();
      if (s.selectWithin) {
        ref.current?.setSelectionRange(start + s.selectWithin[0], start + s.selectWithin[1]);
        setCaret(start + s.selectWithin[1]);
      } else {
        const pos = start + s.insert.length;
        ref.current?.setSelectionRange(pos, pos);
        setCaret(pos);
      }
    });
  }

  function syncCaret() {
    setCaret(ref.current?.selectionStart ?? value.length);
  }

  return (
    <div style={{ position: 'relative' }}>
      <textarea
        ref={ref}
        rows={rows}
        disabled={disabled}
        value={value}
        placeholder={placeholder}
        onChange={(e) => {
          onChange(e.target.value);
          setCaret(e.target.selectionStart ?? e.target.value.length);
          setOpen(true);
        }}
        onKeyUp={syncCaret}
        onClick={syncCaret}
        onFocus={() => setOpen(true)}
        onBlur={() => setTimeout(() => setOpen(false), 150)}
      />
      {open && suggestions.length > 0 && (
        <div
          style={{
            position: 'absolute', zIndex: 20, left: 0, right: 0,
            background: 'var(--cor-branco)', border: '1px solid var(--cor-borda)',
            borderRadius: 'var(--radius-sm)', boxShadow: 'var(--sombra-elevada)',
            maxHeight: 220, overflowY: 'auto',
          }}
        >
          {suggestions.map((s, i) => (
            <div
              key={s.label + i}
              onMouseDown={(e) => {
                e.preventDefault();
                applySuggestion(s);
              }}
              style={{ padding: '0.4rem 0.6rem', cursor: 'pointer', display: 'flex', justifyContent: 'space-between', gap: 8 }}
              onMouseEnter={(e) => (e.currentTarget.style.background = 'var(--cor-fundo)')}
              onMouseLeave={(e) => (e.currentTarget.style.background = 'transparent')}
            >
              <span style={{ fontFamily: 'ui-monospace, monospace', fontSize: '0.8rem' }}>{s.label}</span>
              <span className="muted" style={{ fontSize: '0.7rem' }}>{s.hint}</span>
            </div>
          ))}
        </div>
      )}
    </div>
  );
}

type Mode =
  | { kind: 'external'; term: string }
  | { kind: 'field'; term: string }
  | { kind: 'variable'; term: string }
  | { kind: 'identifier'; term: string }
  | { kind: 'none'; term: string };

/** Determines the autocomplete mode from the text before the caret. */
function detectMode(prefix: string): Mode {
  // External: inside an unclosed '['.
  const ob = prefix.lastIndexOf('[');
  if (ob > prefix.lastIndexOf(']')) {
    return { kind: 'external', term: prefix.slice(ob + 1) };
  }
  // Variable: inside an unclosed '{'.
  const brace = prefix.lastIndexOf('{');
  if (brace > prefix.lastIndexOf('}')) {
    return { kind: 'variable', term: prefix.slice(brace + 1) };
  }
  // Field: inside an unclosed single quote. Count quotes: odd => open.
  const singleQuotes = (prefix.match(/'/g) ?? []).length;
  if (singleQuotes % 2 === 1) {
    const last = prefix.lastIndexOf("'");
    return { kind: 'field', term: prefix.slice(last + 1) };
  }
  // Identifier (function names): a run of letters/digits at the end.
  const token = /([A-Za-zÀ-ÿ0-9_.]*)$/.exec(prefix)?.[1] ?? '';
  return { kind: token.length > 0 ? 'identifier' : 'none', term: token };
}

/** Extracts the arg list from a signature like NOME(a; b; c) -> "a; b; c". */
function argsFromSignature(signature: string): string {
  const open = signature.indexOf('(');
  const close = signature.lastIndexOf(')');
  if (open < 0 || close < 0 || close <= open + 1) return '';
  return signature.slice(open + 1, close);
}
