import type { Session } from './types';

export class ApiError extends Error {
  constructor(message: string, public code: string, public correlationId: string, public status: number) { super(message); }
}

export class ApplicationApi {
  private csrf = '';
  async session(): Promise<Session> {
    const r = await fetch('/api/session', { credentials: 'same-origin', cache: 'no-store' });
    if (!r.ok) throw new Error('Impossible de joindre le service applicatif.');
    const session: Session = await r.json(); this.csrf = session.csrfToken; return session;
  }
  async login(role: string) { await this.auth('/auth/demo-login', { role }); return this.session(); }
  async logout() { await this.auth('/auth/logout', {}); return this.session(); }
  async windows() {
    const r = await fetch('/auth/windows', { credentials: 'same-origin' });
    if (!r.ok) throw new Error('Authentification Windows impossible. Vérifiez votre session de domaine.');
    return this.session();
  }
  private async auth(path: string, body: object) {
    const r = await fetch(path, { method: 'POST', credentials: 'same-origin', headers: { 'Content-Type': 'application/json', 'X-CSRF-TOKEN': this.csrf }, body: JSON.stringify(body) });
    if (!r.ok) { const e = await r.json(); throw new Error(e.message || 'Connexion impossible.'); }
  }
  // This abstraction keeps every screen independent of the network/proxy implementation.
  async call<T>(operation: string, payload: object = {}): Promise<T> {
    const response = await fetch('/api/operations', {
      method: 'POST', credentials: 'same-origin', cache: 'no-store',
      headers: { 'Content-Type': 'application/json', 'X-CSRF-TOKEN': this.csrf },
      body: JSON.stringify({ requestId: crypto.randomUUID(), client: { kind: 'killerbee-web', version: '1.0' }, operation, payload })
    });
    const result = await response.json();
    if (!response.ok || result.status !== 'success') throw new ApiError(result.message || 'Le traitement a échoué.', result.code, result.correlationId, response.status);
    return result.data as T;
  }
}
export const api = new ApplicationApi();
