import { Link, NavLink, Outlet } from 'react-router-dom';

/**
 * Application shell following the UseBens/OKR design system: a fixed brand-blue
 * sidebar with the wordmark and nav, and a content area with a light topbar.
 */
export function AppLayout() {
  return (
    <div className="app-layout">
      <aside className="sidebar">
        <div className="sidebar__header">
          <Link to="/" className="sidebar__brand">
            <span className="sidebar__mark">M</span>
            <span>Motor de Decisão</span>
          </Link>
        </div>
        <nav className="sidebar__nav">
          <NavLink to="/" end className={({ isActive }) => `sidebar__link ${isActive ? 'active' : ''}`}>
            <svg className="sidebar__icon" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2">
              <rect x="3" y="3" width="7" height="7" />
              <rect x="14" y="3" width="7" height="7" />
              <rect x="3" y="14" width="7" height="7" />
              <rect x="14" y="14" width="7" height="7" />
            </svg>
            <span>Políticas</span>
          </NavLink>
          <NavLink to="/fontes" className={({ isActive }) => `sidebar__link ${isActive ? 'active' : ''}`}>
            <svg className="sidebar__icon" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2">
              <ellipse cx="12" cy="5" rx="9" ry="3" />
              <path d="M3 5v14c0 1.66 4 3 9 3s9-1.34 9-3V5" />
              <path d="M3 12c0 1.66 4 3 9 3s9-1.34 9-3" />
            </svg>
            <span>Fontes</span>
          </NavLink>
        </nav>
        <div className="sidebar__foot">Usebens · motor de crédito estilo Crivo</div>
      </aside>

      <div className="content">
        <header className="topbar">
          <h2>Motor de Decisão</h2>
        </header>
        <Outlet />
      </div>
    </div>
  );
}
