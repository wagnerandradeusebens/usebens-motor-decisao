import {
  Component,
  ElementRef,
  computed,
  effect,
  input,
  output,
  signal,
  viewChild,
} from '@angular/core';
import type { SourceDescriptorDto } from '../api/models';
import { FUNCTIONS } from './function-catalog';

interface Suggestion {
  /** Texto inserido, substituindo os últimos `replaceLen` caracteres antes do cursor. */
  insert: string;
  /** Quantos caracteres antes do cursor a inserção substitui (o "termo" digitado). */
  replaceLen: number;
  label: string;
  hint: string;
  /** Após inserir, seleciona o intervalo [start,end] relativo ao texto inserido. */
  selectWithin?: [number, number];
  /** Mantém o dropdown aberto após inserir (refs externas multi-parte). */
  keepOpen?: boolean;
}

type Mode =
  | { kind: 'external'; term: string }
  | { kind: 'field'; term: string }
  | { kind: 'variable'; term: string }
  | { kind: 'policy'; term: string }
  | { kind: 'tableName'; term: string }
  | { kind: 'tableColumn'; term: string; table: string }
  | { kind: 'identifier'; term: string }
  | { kind: 'none'; term: string };

/**
 * Editor de fórmula com autocomplete da linguagem pt-BR:
 * - `'` → campos da proposta (insere 'campo')
 * - `{` → variáveis (insere {variavel})
 * - `[` → percorre Fonte → Produto → Dado do catálogo de fontes
 * - letras → funções (com placeholders)
 * Navegação por teclado (↑/↓, Tab/Enter, Esc) e dropdown ancorado ao cursor.
 */
@Component({
  selector: 'app-formula-input',
  imports: [],
  template: `
    <div class="formula">
      <textarea
        #ta
        class="formula__input"
        [rows]="rows()"
        [disabled]="disabled()"
        [value]="text()"
        [placeholder]="placeholder()"
        (input)="onInput($event)"
        (keydown)="onKeydown($event)"
        (keyup)="syncCaret()"
        (click)="syncCaret()"
        (focus)="open.set(true)"
        (blur)="onBlur()"
      ></textarea>

      <!-- Mirror invisível para calcular a posição do cursor (x/y). -->
      <div #mirror class="formula__mirror" aria-hidden="true"></div>

      @if (open() && suggestions().length > 0) {
        <div
          #dropdown
          class="formula__dropdown"
          [style.left.px]="caretPos().x"
          [style.top.px]="caretPos().y"
        >
          @for (s of suggestions(); track s.label + $index) {
            <div
              class="formula__option"
              [attr.data-index]="$index"
              [class.formula__option--active]="$index === activeIndex()"
              (mousedown)="applySuggestion(s, $event)"
              (mouseenter)="activeIndex.set($index)"
            >
              <span class="formula__option-label">{{ s.label }}</span>
              <span class="formula__option-hint">{{ s.hint }}</span>
            </div>
          }
        </div>
      }
    </div>
  `,
  styles: `
    .formula { position: relative; }
    .formula__input {
      width: 100%;
      box-sizing: border-box;
      font-family: ui-monospace, SFMono-Regular, Menlo, monospace;
      font-size: 0.85rem;
      line-height: 1.5;
      padding: 0.6rem 0.7rem;
      border: 1px solid var(--cor-borda);
      border-radius: var(--radius-sm);
      resize: vertical;
      color: var(--cor-texto);
      background: #fff;
    }
    .formula__input:focus {
      outline: none;
      border-color: var(--cor-primaria);
    }
    /* Espelha o textarea para medir a posição do caret; fora da tela. */
    .formula__mirror {
      position: absolute;
      top: 0;
      left: 0;
      visibility: hidden;
      white-space: pre-wrap;
      word-wrap: break-word;
      overflow-wrap: break-word;
      pointer-events: none;
      font-family: ui-monospace, SFMono-Regular, Menlo, monospace;
      font-size: 0.85rem;
      line-height: 1.5;
      padding: 0.6rem 0.7rem;
      border: 1px solid transparent;
      box-sizing: border-box;
    }
    .formula__dropdown {
      position: absolute;
      z-index: 30;
      min-width: 220px;
      max-width: 340px;
      background: #fff;
      border: 1px solid var(--cor-borda);
      border-radius: var(--radius-sm);
      box-shadow: var(--sombra-elev);
      /* ~8 itens visíveis; acima disso, rola. */
      max-height: 264px;
      overflow-y: auto;
      scrollbar-width: thin;
    }
    .formula__option {
      display: flex;
      align-items: center;
      justify-content: space-between;
      gap: 0.5rem;
      padding: 0.4rem 0.6rem;
      cursor: pointer;
    }
    .formula__option--active { background: var(--cor-fundo); }
    .formula__option-label {
      font-family: ui-monospace, SFMono-Regular, Menlo, monospace;
      font-size: 0.8rem;
      color: var(--cor-texto);
    }
    .formula__option-hint {
      font-size: 0.7rem;
      color: var(--cor-texto-claro);
      flex-shrink: 0;
    }
  `,
})
export class FormulaInput {
  readonly value = input<string>('');
  readonly fields = input<string[]>([]);
  readonly variables = input<string[]>([]);
  readonly sources = input<SourceDescriptorDto[]>([]);
  /** Nomes de todas as políticas (gatilho '$[' de referência cruzada). */
  readonly policies = input<string[]>([]);
  /**
   * Variáveis de cada política (por nome), para o 3º nível da referência cruzada
   * (Política;Variaveis;___): sugere as variáveis da política ALVO, não as locais.
   */
  readonly policyVariables = input<Record<string, string[]>>({});
  /**
   * Tabelas de parâmetros disponíveis (locais + globais), para o autocomplete de
   * PROCV/PROCV.FAIXA: sugere nomes de tabela (1º argumento) e colunas da tabela
   * escolhida (2º argumento).
   */
  readonly tables = input<{ name: string; columns: string[] }[]>([]);
  readonly placeholder = input<string>('');
  readonly rows = input<number>(6);
  readonly disabled = input<boolean>(false);

  readonly valueChange = output<string>();

  private readonly ta = viewChild<ElementRef<HTMLTextAreaElement>>('ta');
  private readonly mirror = viewChild<ElementRef<HTMLDivElement>>('mirror');
  private readonly dropdown = viewChild<ElementRef<HTMLDivElement>>('dropdown');

  protected readonly open = signal(false);
  protected readonly activeIndex = signal(0);
  protected readonly caretPos = signal<{ x: number; y: number }>({ x: 0, y: 0 });
  private readonly caret = signal(0);

  /**
   * Texto atual do editor mantido internamente. O componente é zoneless: se o
   * autocomplete lesse o input `value()` (controlado pelo pai), ele ficaria um
   * ciclo atrás do que o usuário acabou de digitar, e `detectMode` rodaria sobre
   * texto desalinhado com o caret — o que fazia as sugestões não aparecerem.
   * Aqui o `onInput` atualiza `text` a partir do próprio textarea, então
   * `mode`/`suggestions` sempre refletem o conteúdo real e imediato.
   */
  private readonly text = signal('');

  private readonly mode = computed<Mode>(() => detectMode(this.text().slice(0, this.caret())));

  protected readonly suggestions = computed<Suggestion[]>(() => {
    const mode = this.mode();
    switch (mode.kind) {
      case 'external':
        return this.externalSuggestions(mode.term);
      case 'policy':
        return this.policySuggestions(mode.term);
      case 'tableName': {
        // 1º arg de PROCV: nome da tabela. Insere o nome + fecha as aspas.
        const t = mode.term.toLowerCase();
        return this.tables()
          .filter((tb) => tb.name.toLowerCase().includes(t))
          .map((tb) => ({ insert: tb.name + '"', replaceLen: mode.term.length, label: tb.name, hint: 'tabela' }));
      }
      case 'tableColumn': {
        // 2º arg de PROCV: coluna da tabela nomeada no 1º arg.
        const t = mode.term.toLowerCase();
        const tb = this.tables().find((x) => x.name.toLowerCase() === mode.table.toLowerCase());
        return (tb?.columns ?? [])
          .filter((c) => c.toLowerCase().includes(t))
          .map((c) => ({ insert: c + '"', replaceLen: mode.term.length, label: c, hint: 'coluna' }));
      }
      case 'field': {
        const t = mode.term.toLowerCase();
        return this.fields()
          .filter((f) => f.toLowerCase().includes(t))
          .map((f) => ({ insert: f + "'", replaceLen: mode.term.length, label: f, hint: 'campo' }));
      }
      case 'variable': {
        const t = mode.term.toLowerCase();
        return this.variables()
          .filter((v) => v.toLowerCase().includes(t))
          .map((v) => ({ insert: v + '}', replaceLen: mode.term.length, label: v, hint: 'variável' }));
      }
      case 'identifier': {
        const t = mode.term.toLowerCase();
        if (t.length < 1) return [];
        // Literais booleanos nativos (o backend reconhece VERDADEIRO/FALSO).
        const booleans: Suggestion[] = ['VERDADEIRO', 'FALSO']
          .filter((b) => b.toLowerCase().startsWith(t))
          .map((b) => ({ insert: b, replaceLen: mode.term.length, label: b, hint: 'booleano' }));
        const fns = FUNCTIONS.filter((f) => f.name.toLowerCase().startsWith(t)).map<Suggestion>((f) => {
          const inner = argsFromSignature(f.signature);
          const insert = `${f.name}(${inner})`;
          const firstArgLen = inner.split(';')[0]?.length ?? 0;
          const startSel = f.name.length + 1;
          return {
            insert,
            replaceLen: mode.term.length,
            label: f.signature,
            hint: 'função',
            selectWithin: firstArgLen > 0 ? [startSel, startSel + firstArgLen] : undefined,
          };
        });
        return [...booleans, ...fns];
      }
      default:
        return [];
    }
  });

  constructor() {
    // Sincroniza o texto interno quando o pai muda o `value` de fora (carregar
    // uma variável para edição, reset, etc.).
    effect(() => this.text.set(this.value()));

    // Reposiciona o dropdown e reseta o item ativo quando as sugestões mudam.
    effect(() => {
      this.suggestions();
      this.activeIndex.set(0);
      this.updateCaretPosition();
    });
  }

  /**
   * Sugestões de ref externa. Cada parte (fonte/produto/dado) substitui APENAS o
   * segmento atual (após o último ';'), preservando os anteriores já digitados.
   */
  private externalSuggestions(term: string): Suggestion[] {
    const parts = term.split(';');
    const sources = this.sources();
    // O termo do último segmento é o que será substituído ao inserir.
    const segTerm = parts[parts.length - 1];
    const replaceLen = segTerm.length;

    if (parts.length === 1) {
      const t = segTerm.trim().toLowerCase();
      return sources
        .filter((s) => s.name.toLowerCase().includes(t))
        .map((s) => ({ insert: s.name + ';', replaceLen, label: s.name, hint: 'fonte', keepOpen: true }));
    }
    if (parts.length === 2) {
      const src = sources.find((s) => s.name.toLowerCase() === parts[0].trim().toLowerCase());
      const t = segTerm.trim().toLowerCase();
      return (src?.products ?? [])
        .filter((p) => p.name.toLowerCase().includes(t))
        .map((p) => ({ insert: p.name + ';', replaceLen, label: p.name, hint: 'produto', keepOpen: true }));
    }
    if (parts.length === 3) {
      const src = sources.find((s) => s.name.toLowerCase() === parts[0].trim().toLowerCase());
      const prod = src?.products.find((p) => p.name.toLowerCase() === parts[1].trim().toLowerCase());
      const t = segTerm.trim().toLowerCase();
      return (prod?.data ?? [])
        .filter((d) => d.name.toLowerCase().includes(t))
        .map((d) => ({ insert: d.name + ']', replaceLen, label: d.name, hint: 'dado' }));
    }
    return [];
  }

  /**
   * Sugestões de referência a política com `$[`: `$[Política;Categoria]` ou
   * `$[Política;Variaveis;variável]`. O conteúdo entre `$[` e `]` é cru (sem
   * aspas), então o nome pode ter qualquer caractere. Cada segmento substitui
   * apenas a parte atual (após o último ';'); o último segmento fecha com ']'.
   */
  private policySuggestions(term: string): Suggestion[] {
    const parts = term.split(';');
    const segTerm = parts[parts.length - 1];
    const replaceLen = segTerm.length;
    const t = segTerm.trim().toLowerCase();

    // Nível 1: a política — lista todas as políticas.
    if (parts.length === 1) {
      return this.policies()
        .filter((p) => p.toLowerCase().includes(t))
        .map((p) => ({ insert: `${p};`, replaceLen, label: p, hint: 'política', keepOpen: true }));
    }

    // Nível 2: a categoria. Pontos/Limite/Resposta fecham a referência com ']';
    // Variaveis mantém aberto para escolher a variável no nível 3.
    if (parts.length === 2) {
      const cats = [
        { name: 'Pontos', hint: 'acumulado' },
        { name: 'Limite', hint: 'acumulado' },
        { name: 'Resposta', hint: 'string' },
        { name: 'Variaveis', hint: 'lista' },
      ];
      return cats
        .filter((c) => c.name.toLowerCase().includes(t))
        .map((c) =>
          c.name === 'Variaveis'
            ? { insert: 'Variaveis;', replaceLen, label: c.name, hint: c.hint, keepOpen: true }
            : { insert: `${c.name}]`, replaceLen, label: c.name, hint: c.hint },
        );
    }

    // Nível 3: a variável (quando categoria = Variaveis) — fecha com ']'.
    // As variáveis são as da política ALVO (1º segmento), não as locais.
    if (parts.length === 3 && parts[1].trim().toLowerCase().startsWith('vari')) {
      const policyName = parts[0].trim();
      const map = this.policyVariables();
      const targetVars =
        map[policyName] ??
        // fallback tolerante a caixa: casa o nome ignorando maiúsc./minúsc.
        map[Object.keys(map).find((k) => k.toLowerCase() === policyName.toLowerCase()) ?? ''] ??
        [];
      return targetVars
        .filter((v) => v.toLowerCase().includes(t))
        .map((v) => ({ insert: `${v}]`, replaceLen, label: v, hint: 'variável' }));
    }
    return [];
  }

  protected onInput(event: Event): void {
    const el = event.target as HTMLTextAreaElement;
    // Atualiza o texto interno primeiro (fonte da verdade para o autocomplete),
    // depois notifica o pai. Assim o computed de sugestões enxerga o valor novo
    // no mesmo instante, sem depender do ciclo de CD do pai (app zoneless).
    this.text.set(el.value);
    this.caret.set(el.selectionStart ?? el.value.length);
    this.valueChange.emit(el.value);
    this.open.set(true);
  }

  protected onKeydown(event: KeyboardEvent): void {
    const hasSuggestions = this.open() && this.suggestions().length > 0;

    // Sem dropdown aberto: Tab e Enter servem à indentação para fórmulas
    // multi-linha. Com dropdown, eles selecionam a sugestão (tratado abaixo).
    if (!hasSuggestions) {
      if (event.key === 'Tab') {
        event.preventDefault();
        this.indentAtCursor(event.shiftKey);
        return;
      }
      if (event.key === 'Enter') {
        event.preventDefault();
        this.newlineKeepingIndent();
        return;
      }
      return;
    }

    const max = this.suggestions().length - 1;
    switch (event.key) {
      case 'ArrowDown':
        event.preventDefault();
        this.activeIndex.set(Math.min(this.activeIndex() + 1, max));
        this.scrollActiveIntoView();
        break;
      case 'ArrowUp':
        event.preventDefault();
        this.activeIndex.set(Math.max(this.activeIndex() - 1, 0));
        this.scrollActiveIntoView();
        break;
      case 'Enter':
      case 'Tab': {
        const s = this.suggestions()[this.activeIndex()];
        if (s) {
          event.preventDefault();
          this.insert(s);
        }
        break;
      }
      case 'Escape':
        event.preventDefault();
        this.open.set(false);
        break;
    }
  }

  /** Largura da indentação (2 espaços) usada por Tab/auto-indent. */
  private static readonly INDENT = '  ';

  /**
   * Tab insere um nível de indentação na posição do cursor; Shift+Tab remove um
   * nível (até 2 espaços) do início da linha atual. Mantém o foco no campo.
   */
  private indentAtCursor(outdent: boolean): void {
    const el = this.ta()?.nativeElement;
    if (!el) return;
    const value = el.value;
    const start = el.selectionStart ?? value.length;
    const end = el.selectionEnd ?? start;

    if (outdent) {
      // Remove até 2 espaços no início da linha onde o cursor está.
      const lineStart = value.lastIndexOf('\n', start - 1) + 1;
      const removable = value.slice(lineStart).match(/^ {1,2}/)?.[0].length ?? 0;
      if (removable === 0) return;
      const next = value.slice(0, lineStart) + value.slice(lineStart + removable);
      this.commit(next, Math.max(lineStart, start - removable));
      return;
    }

    const indent = FormulaInput.INDENT;
    const next = value.slice(0, start) + indent + value.slice(end);
    this.commit(next, start + indent.length);
  }

  /** Enter insere uma nova linha preservando a indentação da linha atual. */
  private newlineKeepingIndent(): void {
    const el = this.ta()?.nativeElement;
    if (!el) return;
    const value = el.value;
    const start = el.selectionStart ?? value.length;
    const end = el.selectionEnd ?? start;
    const lineStart = value.lastIndexOf('\n', start - 1) + 1;
    const currentIndent = value.slice(lineStart, start).match(/^ */)?.[0] ?? '';
    const insertText = '\n' + currentIndent;
    const next = value.slice(0, start) + insertText + value.slice(end);
    this.commit(next, start + insertText.length);
  }

  /** Aplica um novo texto ao textarea e reposiciona o cursor, sincronizando o estado. */
  private commit(next: string, caret: number): void {
    const el = this.ta()?.nativeElement;
    if (!el) return;
    el.value = next;
    el.setSelectionRange(caret, caret);
    this.text.set(next);
    this.caret.set(caret);
    this.valueChange.emit(next);
    this.updateCaretPosition();
  }

  protected syncCaret(): void {
    const el = this.ta()?.nativeElement;
    this.caret.set(el?.selectionStart ?? this.text().length);
    this.updateCaretPosition();
  }

  /** Garante que o item ativo fique visível ao navegar com as setas. */
  private scrollActiveIntoView(): void {
    requestAnimationFrame(() => {
      const dd = this.dropdown()?.nativeElement;
      const opt = dd?.querySelector<HTMLElement>(`[data-index="${this.activeIndex()}"]`);
      opt?.scrollIntoView({ block: 'nearest' });
    });
  }

  protected onBlur(): void {
    setTimeout(() => this.open.set(false), 150);
  }

  protected applySuggestion(s: Suggestion, event: MouseEvent): void {
    event.preventDefault();
    this.insert(s);
  }

  /** Aplica a sugestão substituindo apenas os `replaceLen` chars do termo atual. */
  private insert(s: Suggestion): void {
    const value = this.text();
    const caret = this.caret();
    const start = caret - s.replaceLen;
    const next = value.slice(0, start) + s.insert + value.slice(caret);
    this.text.set(next);
    this.valueChange.emit(next);
    this.open.set(Boolean(s.keepOpen));

    requestAnimationFrame(() => {
      const el = this.ta()?.nativeElement;
      if (!el) return;
      el.value = next;
      el.focus();
      if (s.selectWithin) {
        el.setSelectionRange(start + s.selectWithin[0], start + s.selectWithin[1]);
        this.caret.set(start + s.selectWithin[1]);
      } else {
        const pos = start + s.insert.length;
        el.setSelectionRange(pos, pos);
        this.caret.set(pos);
      }
      this.updateCaretPosition();
    });
  }

  /**
   * Calcula a posição (x/y) do cursor dentro do textarea usando um "mirror" com
   * o mesmo estilo, e ancora o dropdown logo abaixo dessa posição.
   */
  private updateCaretPosition(): void {
    const el = this.ta()?.nativeElement;
    const mirror = this.mirror()?.nativeElement;
    if (!el || !mirror) return;

    mirror.style.width = `${el.clientWidth}px`;
    const before = this.text().slice(0, this.caret());
    // Texto até o cursor + um marcador para medir a posição.
    mirror.textContent = before;
    const marker = document.createElement('span');
    marker.textContent = '\u200b';
    mirror.appendChild(marker);

    const x = marker.offsetLeft - el.scrollLeft;
    const y = marker.offsetTop - el.scrollTop;
    const lineHeight = 0.85 * 16 * 1.5; // font-size * line-height aprox.
    this.caretPos.set({ x: Math.min(x, el.clientWidth - 220), y: y + lineHeight + 4 });
  }
}

/** Determina o modo do autocomplete a partir do texto antes do cursor. */
function detectMode(prefix: string): Mode {
  // Autocomplete de PROCV/PROCV.FAIXA: quando o cursor está dentro de um
  // argumento de texto ("...") desses. 1º arg → nome de tabela; 2º arg →
  // coluna. Verificado antes das aspas simples/texto para não cair em "campo".
  const table = detectTableContext(prefix);
  if (table) return table;

  // Referência de política: $[Política;Categoria;Variável]. Detectada quando há
  // um '$[' ainda não fechado por ']'. Verificada ANTES do '[' de fonte externa
  // para o colchete logo após '$' não ser confundido com fonte. Sem ambiguidade
  // com '(' — o nome pode ter qualquer caractere (parênteses, espaços, hífen).
  const policyOpen = prefix.lastIndexOf('$[');
  if (policyOpen >= 0 && prefix.indexOf(']', policyOpen) === -1) {
    return { kind: 'policy', term: prefix.slice(policyOpen + 2) };
  }
  const ob = prefix.lastIndexOf('[');
  if (ob > prefix.lastIndexOf(']')) {
    // Um '[' precedido de '$' é referência de política (tratada acima); aqui é
    // fonte externa só quando NÃO for o colchete do '$['.
    if (ob === 0 || prefix[ob - 1] !== '$') {
      return { kind: 'external', term: prefix.slice(ob + 1) };
    }
  }
  const brace = prefix.lastIndexOf('{');
  if (brace > prefix.lastIndexOf('}')) {
    return { kind: 'variable', term: prefix.slice(brace + 1) };
  }
  const singleQuotes = (prefix.match(/'/g) ?? []).length;
  if (singleQuotes % 2 === 1) {
    const last = prefix.lastIndexOf("'");
    return { kind: 'field', term: prefix.slice(last + 1) };
  }
  const token = /([A-Za-zÀ-ÿ0-9_.]*)$/.exec(prefix)?.[1] ?? '';
  return { kind: token.length > 0 ? 'identifier' : 'none', term: token };
}

/**
 * Detecta se o cursor está dentro de um argumento de texto de PROCV/PROCV.FAIXA.
 * Localiza a chamada aberta mais recente (parêntese sem fechar após o nome),
 * conta o argumento atual pelos ';' de nível superior e verifica se estamos
 * dentro de aspas duplas. 1º arg → nome de tabela; 2º arg → coluna. Retorna
 * null quando não se aplica.
 */
function detectTableContext(prefix: string): Mode | null {
  // Acha o '(' aberto mais recente e verifica se é precedido por PROCV/PROCV.FAIXA.
  let depth = 0;
  let callOpen = -1;
  for (let i = prefix.length - 1; i >= 0; i--) {
    const ch = prefix[i];
    if (ch === ')') depth++;
    else if (ch === '(') {
      if (depth === 0) { callOpen = i; break; }
      depth--;
    }
  }
  if (callOpen < 0) return null;

  const before = prefix.slice(0, callOpen);
  const fn = /(PROCV\.FAIXA|PROCV)$/i.exec(before);
  if (!fn) return null;

  // Conteúdo entre o '(' e o cursor. Conta argumentos por ';' fora de aspas.
  const inner = prefix.slice(callOpen + 1);
  let argIndex = 0;
  let inQuotes = false;
  let quoteStart = -1;
  for (let i = 0; i < inner.length; i++) {
    const ch = inner[i];
    if (ch === '"') {
      inQuotes = !inQuotes;
      if (inQuotes) quoteStart = i;
    } else if (ch === ';' && !inQuotes) {
      argIndex++;
    }
  }
  // Só sugere quando o cursor está DENTRO de aspas (digitando o texto do arg).
  if (!inQuotes || quoteStart < 0) return null;

  const term = inner.slice(quoteStart + 1);
  if (argIndex === 0) return { kind: 'tableName', term };
  if (argIndex === 1) {
    // Extrai o nome da tabela do 1º argumento (primeiro "..." do inner).
    const firstArg = /"([^"]*)"/.exec(inner);
    return { kind: 'tableColumn', term, table: firstArg?.[1] ?? '' };
  }
  return null;
}

/** Extrai a lista de args de uma assinatura NOME(a; b; c) -> "a; b; c". */
function argsFromSignature(signature: string): string {
  const open = signature.indexOf('(');
  const close = signature.lastIndexOf(')');
  if (open < 0 || close < 0 || close <= open + 1) return '';
  return signature.slice(open + 1, close);
}
