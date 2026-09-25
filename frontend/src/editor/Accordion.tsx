import { useState, type ReactNode } from 'react';

interface Props {
  title: string;
  defaultOpen?: boolean;
  children: ReactNode;
}

/** A single collapsible section for the editor's left sidebar. */
export function Accordion({ title, defaultOpen = false, children }: Props) {
  const [open, setOpen] = useState(defaultOpen);
  return (
    <div className="accordion">
      <button className="accordion__head" onClick={() => setOpen((o) => !o)} type="button">
        <span>{title}</span>
        <span className={`accordion__chev ${open ? 'open' : ''}`}>›</span>
      </button>
      {open && <div className="accordion__body">{children}</div>}
    </div>
  );
}
