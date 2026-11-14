const byId = id => document.getElementById(id);
const form = byId('staff-form');
const session = byId('staff-session');
const button = byId('login-button');
const errorMessage = byId('login-error');
const next = new URLSearchParams(location.search).get('returnTo') ?? '';
const returnTo = /^\/c\/[A-Za-z0-9_-]{32}\/?$/.test(next) ? next : null;

if (returnTo) {
  byId('back-link').href = returnTo;
  byId('back-link').textContent = '← Вернуться к карточке';
}

function showError(message) {
  errorMessage.textContent = message;
  errorMessage.hidden = false;
}

function setSession(signedIn) {
  if (signedIn && returnTo) {
    location.replace(returnTo);
    return;
  }
  form.hidden = signedIn;
  session.hidden = !signedIn;
}

form.addEventListener('submit', async event => {
  event.preventDefault();
  button.disabled = true;
  errorMessage.hidden = true;
  try {
    const response = await fetch('/api/staff/login', {
      method: 'POST',
      headers: { 'Content-Type': 'application/json', 'X-ExtraMed': '1' },
      body: JSON.stringify({ password: byId('password').value })
    });
    if (response.ok) {
      form.reset();
      setSession(true);
    } else {
      const error = await response.json().catch(() => ({}));
      showError(error.message || (response.status === 429 ? 'Слишком много попыток. Повторите через две минуты.' : 'Не удалось войти.'));
    }
  } catch {
    showError('Проверьте подключение и попробуйте ещё раз.');
  } finally {
    button.disabled = false;
  }
});

byId('logout-button').addEventListener('click', async event => {
  const logout = event.currentTarget;
  logout.disabled = true;
  try {
    const response = await fetch('/api/staff/logout', { method: 'POST', headers: { 'X-ExtraMed': '1' } });
    if (!response.ok) throw new Error();
    setSession(false);
    byId('password').focus();
  } catch {
    session.querySelector('[role="status"]').textContent = 'Не удалось выйти. Проверьте подключение и повторите.';
  } finally {
    logout.disabled = false;
  }
});

fetch('/api/staff/session').then(response => response.json()).then(current => {
  if (!current.enabled) showError('Вход для сотрудников пока не настроен.');
  else setSession(current.signedIn);
}).catch(() => {});
