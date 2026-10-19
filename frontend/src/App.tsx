import { useEffect, useState } from 'react';
import { ArrowRight, Boxes, ClipboardCheck, FlaskConical, LayoutDashboard, LogOut, Network, PanelLeftClose, ShieldCheck, SlidersHorizontal, Workflow } from 'lucide-react';
import { api } from './api';
import type { Audit, Dashboard, Identity, Model, Session } from './types';
import { Badge, Empty, ErrorBox, Loading, dateTime, useData } from './ui';
import { ModelsPage } from './Models';
import { IngredientsPage, CharacteristicsPage } from './ReferenceData';
import { ProcessesPage } from './Processes';
import { TestsPage } from './Tests';

type Page = 'dashboard' | 'models' | 'ingredients' | 'characteristics' | 'processes' | 'tests' | 'audit' | 'identity';
const pages = [
  { id: 'dashboard', label: 'Vue d’ensemble', icon: LayoutDashboard },
  { id: 'models', label: 'Modèles', icon: Boxes },
  { id: 'ingredients', label: 'Ingrédients', icon: FlaskConical },
  { id: 'characteristics', label: 'Caractéristiques', icon: SlidersHorizontal },
  { id: 'processes', label: 'Procédés', icon: Workflow },
  { id: 'tests', label: 'Tests & validations', icon: ClipboardCheck },
  { id: 'audit', label: 'Traçabilité', icon: ShieldCheck },
  { id: 'identity', label: 'Ma connexion', icon: Network }
] as const;
const roleNames: Record<string, string> = { RD: 'Recherche & développement', TEST: 'Département de tests', PROD: 'Production' };

export function App() {
  const [session, setSession] = useState<Session | null>(null);
  const [error, setError] = useState<Error | null>(null);
  const [page, setPage] = useState<Page>('dashboard');
  const [modelFilter, setModelFilter] = useState('');
  const [sidebar, setSidebar] = useState(false);
  async function refresh() { try { setSession(await api.session()); setError(null); } catch (e) { setError(e as Error); } }
  useEffect(() => { void refresh(); }, []);
  const navigate = (next: Page, model = '') => { setPage(next); setModelFilter(model); setSidebar(false); };
  if (!session) return <div className="login-shell">{error ? <ErrorBox error={error} retry={refresh} /> : <Loading />}</div>;
  if (!session.user) return <Login session={session} onLogin={setSession} />;
  const roles = session.user.roles;
  const rd = roles.includes('RD');
  const productionOnly = roles.includes('PROD') && !roles.includes('RD') && !roles.includes('TEST');
  const allowed = pages.filter(p => (!productionOnly || !['ingredients', 'characteristics', 'tests', 'audit'].includes(p.id)) && (p.id !== 'audit' || rd));
  async function logout() { try { setSession(await api.logout()); setPage('dashboard'); } catch (e) { setError(e as Error); } }
  return <div className="app-shell">
    <aside className={`sidebar ${sidebar ? 'open' : ''}`}>
      <a className="brand" href="#" onClick={e => { e.preventDefault(); navigate('dashboard'); }}><span className="brand-mark">K<span> B</span></span><span>KillerBee<small>ATELIER DE FABRICATION</small></span></a>
      <p className="nav-caption">ESPACE DE TRAVAIL</p>
      <nav aria-label="Navigation principale">{allowed.map(p => <button key={p.id} className={page === p.id ? 'active' : ''} aria-current={page === p.id ? 'page' : undefined} onClick={() => navigate(p.id)}><p.icon size={19} />{p.label}</button>)}</nav>
      <div className="sidebar-bottom"><ShieldCheck size={20} /><strong>Un savoir-faire protégé.</strong><p>Des recettes à la fabrication,<br />chaque étape est tracée.</p><span className="internal-label">APPLICATION INTERNE</span></div>
    </aside>
    <div className="main-shell"><header className="topbar"><button className="icon-button menu-toggle" aria-label="Afficher le menu" onClick={() => setSidebar(!sidebar)}><PanelLeftClose size={20} /></button>
      <span className="breadcrumb">Espace KillerBee <span>/</span> {pages.find(p => p.id === page)?.label}</span>
      <div className="user-block">{session.demo && <span className="demo-label">Démonstration locale</span>}<div className="avatar">{roles[0] || 'KB'}</div><div><strong>{session.user.name}</strong><small>{roles.map(r => roleNames[r]).join(', ') || 'Aucun service autorisé'}</small></div>
        {session.demo && <button className="icon-button" title="Changer de profil" aria-label="Changer de profil" onClick={logout}><LogOut size={18} /></button>}</div>
    </header><main>{error && <ErrorBox error={error} />}
      {page === 'dashboard' && <DashboardPage navigate={navigate} />}
      {page === 'models' && <ModelsPage canEdit={rd} onProcesses={id => navigate('processes', id)} />}
      {page === 'ingredients' && <IngredientsPage canEdit={rd} />}
      {page === 'characteristics' && <CharacteristicsPage canEdit={rd} />}
      {page === 'processes' && <ProcessesPage canEdit={rd} initialModel={modelFilter} onTests={id => navigate('tests', id)} />}
      {page === 'tests' && <TestsPage canEdit={rd} canTest={roles.includes('TEST')} initialModel={modelFilter} />}
      {page === 'audit' && <AuditPage />}{page === 'identity' && <IdentityPage />}
    </main><footer className="app-footer">KillerBee · Des idées qui prennent leur envol.<span>R&D · Tests · Production</span></footer></div>
  </div>;
}

function Login({ session, onLogin }: { session: Session; onLogin: (s: Session) => void }) {
  const [busy, setBusy] = useState(false); const [error, setError] = useState<Error | null>(null);
  async function login(role?: string) { setBusy(true); setError(null); try { onLogin(role ? await api.login(role) : await api.windows()); } catch (e) { setError(e as Error); } finally { setBusy(false); } }
  return <div className="login-shell"><div className="login-story"><div className="brand light">KillerBee<span className="little-tag">DEPUIS LES ANNÉES 1960</span></div><h1>Le savoir-faire,<br />à chaque étape.</h1><p>Concevoir. Tester. Fabriquer.<br />Votre atelier de fabrication, réuni au même endroit.</p><div className="disc-art"><div /><div /><div /></div></div>
    <div className="login-panel"><span className="eyebrow">BIENVENUE DANS L’ATELIER</span><h2>Accéder à mon espace</h2><p>{session.demo ? 'Choisissez un service pour explorer les parcours avec des données fictives.' : 'Utilisez votre session Windows du domaine KillerBee.'}</p>
      {error && <ErrorBox error={error} />}{session.demo ? <div className="role-options">{(['RD', 'TEST', 'PROD'] as const).map(r => <button disabled={busy} key={r} onClick={() => login(r)}><span className={`role-icon role-${r.toLowerCase()}`}>{r === 'RD' ? <FlaskConical /> : r === 'TEST' ? <ClipboardCheck /> : <Boxes />}</span><span><strong>{roleNames[r]}</strong><small>{r === 'RD' ? 'Concevoir les recettes et procédés' : r === 'TEST' ? 'Exécuter et valider les tests' : 'Consulter les instructions validées'}</small></span><ArrowRight size={20} /></button>)}</div>
        : <button className="button" disabled={busy} onClick={() => login()}>Se connecter avec Windows <ArrowRight size={18} /></button>}
      <div className="login-note"><ShieldCheck size={18} /><span>{session.demo ? 'Mode pédagogique limité à cette machine. Les comptes AD ne sont pas utilisés dans cette démonstration.' : 'L’accès est réservé aux collaborateurs autorisés.'}</span></div></div></div>;
}

function DashboardPage({ navigate }: { navigate: (p: Page) => void }) {
  const resource = useData(async () => ({ dashboard: await api.call<Dashboard>('dashboard.read'), models: await api.call<Model[]>('models.list') }));
  return <><div className="page-heading"><div><span className="eyebrow">L’ATELIER KILLERBEE</span><h1>Vue d’ensemble</h1><p>Vos modèles, vos procédés et les prochaines étapes.</p></div><span className="today">{new Date().toLocaleDateString('fr-FR', { day: 'numeric', month: 'long', year: 'numeric' })}</span></div>
    {resource.error && <ErrorBox error={resource.error} retry={resource.reload} />}{resource.loading && !resource.data && <Loading />}{resource.data && <>
      <section className="welcome-banner"><div><span className="eyebrow">DE LA RECETTE À L’ATELIER</span><h2>Chaque détail compte.<br />Chaque étape aussi.</h2><p>Retrouvez les compositions, les modes opératoires<br className="desktop-break" /> et les validations de vos gammes.</p><button className="button light-button" onClick={() => navigate('models')}>Ouvrir le catalogue <ArrowRight size={17} /></button></div><div className="banner-disc"><span>KB</span><span>KILLERBEE · FABRICATION</span></div></section>
      <section className="stats-grid" aria-label="Indicateurs">{[{ label: 'Modèles au catalogue', value: resource.data.dashboard.models, icon: Boxes, color: 'purple' }, { label: 'Modèles validés', value: resource.data.dashboard.validated, icon: ShieldCheck, color: 'green' }, { label: 'Procédés définis', value: resource.data.dashboard.processes, icon: Workflow, color: 'orange' }, { label: 'Exécutions à valider', value: resource.data.dashboard.pending, icon: ClipboardCheck, color: 'blue' }].map(s => <article className="stat-card" key={s.label}><span className={`stat-icon ${s.color}`}><s.icon size={22} /></span><strong>{s.value}</strong><span>{s.label}</span></article>)}</section>
      <section className="panel"><div className="panel-heading"><div><h2>Les modèles de votre atelier</h2><p>Un aperçu du catalogue accessible à votre service.</p></div><button className="text-button" onClick={() => navigate('models')}>Tout consulter <ArrowRight size={16} /></button></div><div className="table-scroll"><table><thead><tr><th>MODÈLE</th><th>GAMME</th><th>VERSION</th><th>STATUT</th></tr></thead><tbody>{resource.data.models.slice(0, 6).map(m => <tr key={m.id}><td><strong>{m.name}</strong><small>{m.description}</small></td><td>{m.range}</td><td>v{m.revision}</td><td><Badge state={m.state} /></td></tr>)}</tbody></table></div>{resource.data.models.length === 0 && <Empty>Aucun modèle accessible pour le moment.</Empty>}</section>
      <div className="flow-strip"><span><b>01</b> Concevoir en R&D</span><ArrowRight size={16} /><span><b>02</b> Tester et valider</span><ArrowRight size={16} /><span><b>03</b> Transmettre à la production</span></div>
    </>}</>;
}

function AuditPage() {
  const r = useData(() => api.call<Audit[]>('audit.list'));
  return <><div className="page-heading"><div><span className="eyebrow">HISTORIQUE</span><h1>Traçabilité</h1><p>Les 200 dernières modifications effectuées dans l’application.</p></div><button className="button secondary" onClick={r.reload}>Actualiser</button></div>{r.error && <ErrorBox error={r.error} retry={r.reload} />}{r.loading && !r.data && <Loading />}
    {r.data && <div className="panel table-scroll"><table><thead><tr><th>DATE</th><th>UTILISATEUR</th><th>ACTION</th><th>OBJET / SUIVI</th></tr></thead><tbody>{r.data.map(e => <tr key={e.id}><td>{dateTime(e.at)}</td><td>{e.actor}</td><td><code>{e.operation}</code></td><td><code>{e.entityId}</code><small>Suivi : {e.correlationId}</small></td></tr>)}</tbody></table>{r.data.length === 0 && <Empty>Les prochaines modifications apparaîtront ici.</Empty>}</div>}</>;
}

function IdentityPage() {
  const r = useData(() => api.call<Identity>('system.identity'));
  return <><div className="page-heading"><div><span className="eyebrow">ACCÈS</span><h1>Ma connexion</h1><p>Vérifiez l’identité utilisée par votre session et son accès aux données.</p></div><button className="button secondary" onClick={r.reload}>Vérifier à nouveau</button></div>{r.error && <ErrorBox error={r.error} retry={r.reload} />}{r.loading && <Loading />}{r.data && <section className="panel identity-panel"><ShieldCheck size={32} /><h2>{r.data.demo ? 'Session de démonstration' : 'Session de domaine'}</h2><dl><dt>Utilisateur applicatif</dt><dd>{r.data.user}</dd><dt>Services autorisés</dt><dd>{r.data.roles.map(r => roleNames[r]).join(', ')}</dd><dt>Identité SQL effective</dt><dd>{r.data.sqlIdentity}</dd><dt>Connexion SQL d’origine</dt><dd>{r.data.originalLogin || 'Non applicable à la démonstration'}</dd><dt>Instance ayant traité la demande</dt><dd>{r.data.instance}</dd></dl>{r.data.demo && <p className="info-box">Le mode local utilise SQLite. La validation Active Directory et SQL Server doit être réalisée dans la maquette de votre équipe.</p>}</section>}</>;
}
