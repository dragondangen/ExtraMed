import { patientMeta, pregnancyText } from './patient-display.js';

const byId = id => document.getElementById(id);
const cardId = location.pathname.split('/').filter(Boolean).at(-1);
const api = '/api/cards/' + encodeURIComponent(cardId);
const status = byId('card-status');
const docsStatus = byId('document-status');
const docs = byId('document-values');
const signOut = byId('staff-sign-out');
const signIn = byId('staff-sign-in');

function display(id, value) {
  byId('patient-' + id).textContent = value || 'Не указано';
}

async function loadDocuments() {
  signOut.hidden = true;
  signIn.hidden = true;
  docs.hidden = true;
  display('passport', '');
  display('policy', '');
  docsStatus.hidden = false;
  docsStatus.textContent = 'Проверяем доступ…';
  try {
    const response = await fetch(api + '/documents', { cache: 'no-store' });
    if (response.status === 401) {
      docsStatus.textContent = 'Доступны сотрудникам после входа.';
      signIn.href = '/staff?returnTo=' + encodeURIComponent(location.pathname);
      signIn.hidden = false;
      return;
    }
    if (response.status === 503) {
      docsStatus.textContent = 'Вход для сотрудников пока не настроен.';
      return;
    }
    if (!response.ok) throw new Error();
    const { passport, policy } = await response.json();
    display('passport', passport);
    display('policy', policy);
    docs.hidden = false;
    signOut.hidden = false;
    docsStatus.hidden = true;
  } catch {
    docsStatus.textContent = 'Не удалось загрузить документы. Обновите страницу.';
  }
}

async function loadCard() {
  try {
    const response = await fetch(api, { cache: 'no-store' });
    if (response.status === 404) {
      status.textContent = 'Карточка не найдена. Проверьте ссылку или считайте метку ещё раз.';
      return;
    }
    if (!response.ok) throw new Error();

    const card = await response.json();
    display('name', card.fullName);
    display('meta', patientMeta(card.sex, card.age));
    display('allergies', card.allergies);
    display('medications', card.medications);
    display('history', card.history);
    display('pregnancy', pregnancyText(card.pregnancy, card.pregnancyDetails));
    if (card.photo) {
      const photo = byId('patient-photo');
      photo.addEventListener('load', () => photo.hidden = false, { once: true });
      photo.src = card.photo;
    }
    byId('updated').textContent = 'Анкета заполнена ' + new Date(card.updatedAt).toLocaleDateString('ru-RU', { timeZone: 'Europe/Moscow' });
    status.hidden = true;
    byId('patient-card').hidden = false;
    await loadDocuments();
  } catch {
    status.textContent = 'Не удалось загрузить карточку. Проверьте интернет и обновите страницу.';
  }
}

signOut.addEventListener('click', async () => {
  signOut.disabled = true;
  try {
    const response = await fetch('/api/staff/logout', { method: 'POST', headers: { 'X-ExtraMed': '1' } });
    if (!response.ok) throw new Error();
    await loadDocuments();
    signIn.focus();
  } catch {
    docsStatus.hidden = false;
    docsStatus.textContent = 'Не удалось выйти. Проверьте подключение и повторите.';
  } finally {
    signOut.disabled = false;
  }
});

window.addEventListener('pagehide', () => {
  docs.hidden = true;
  display('passport', '');
  display('policy', '');
});
window.addEventListener('pageshow', event => {
  if (event.persisted && !byId('patient-card').hidden) loadDocuments();
});
loadCard();
