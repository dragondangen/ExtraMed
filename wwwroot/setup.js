import { patientMeta, pregnancyText } from './patient-display.js';

const byId = id => document.getElementById(id);
const form = byId('patient-form');
const photo = byId('photo');
const save = byId('save-card');
const write = byId('write-nfc');
const cancel = byId('cancel-nfc');
const status = byId('nfc-status');
const formError = byId('form-error');
const nfcAvailable = window.isSecureContext && 'NDEFReader' in window;
let photoPreview;
let photoLimitMb = 5;
let savedCard;
let changed = false;
let saving = false;
let writing;

function imagePreview() {
  if (photoPreview) URL.revokeObjectURL(photoPreview);
  const file = photo.files[0];
  photo.setCustomValidity('');
  if (file && file.size > photoLimitMb * 1_000_000) {
    photo.setCustomValidity('Выберите фото размером до ' + photoLimitMb + ' МБ.');
  }
  photoPreview = file && photo.validity.valid ? URL.createObjectURL(file) : null;
  for (const id of ['photo-preview', 'preview-photo']) {
    const image = byId(id);
    image.hidden = !photoPreview;
    if (photoPreview) image.src = photoPreview;
    else image.removeAttribute('src');
  }
  byId('remove-photo').hidden = !file;
}

function renderPreview() {
  const value = id => byId(id).value.trim();
  byId('preview-name').textContent = value('fullName') || 'ФИО пациента';
  byId('preview-meta').textContent = patientMeta(value('sex'), value('age')) || 'Пол и возраст';
  for (const field of ['allergies', 'medications', 'history']) {
    byId('preview-' + field).textContent = value(field) || 'Не указано';
  }
  const pregnancy = value('pregnancy');
  byId('pregnancy-field').hidden = pregnancy !== 'yes';
  byId('pregnancyDetails').disabled = pregnancy !== 'yes';
  byId('preview-pregnancy-line').hidden = pregnancy === 'unknown';
  byId('preview-pregnancy').textContent = pregnancyText(pregnancy, value('pregnancyDetails'));
}

function notice(message, type = '') {
  status.textContent = message;
  status.className = 'notice' + (type ? ' ' + type : '');
  status.hidden = !message;
}

function displaySavedCard() {
  const cardLink = byId('card-link');
  const urlField = byId('nfc-url-field');
  const urlInput = byId('nfc-url');
  cardLink.hidden = !savedCard;
  urlField.hidden = !savedCard?.nfcUrl || changed;
  write.disabled = !savedCard?.nfcUrl || changed || !nfcAvailable || Boolean(writing);
  cancel.hidden = !writing;
  save.disabled = saving || Boolean(savedCard && !changed);
  if (!savedCard) return;

  cardLink.href = savedCard.cardPath;
  urlInput.value = savedCard.nfcUrl || '';
  byId('nfc-summary').textContent = changed
    ? 'Сохраните изменения перед записью метки.'
    : savedCard.nfcUrl ? 'Карточка готова к записи на метку.' : 'Карточка сохранена для локальной проверки.';
}

function onEdit() {
  renderPreview();
  formError.hidden = true;
  if (!savedCard || changed) return;
  changed = true;
  writing?.abort();
  save.firstChild.textContent = 'Создать карточку с изменениями ';
  notice('Кнопка «Открыть карточку» ведёт к прошлой версии анкеты.');
  displaySavedCard();
}

form.addEventListener('input', onEdit);
photo.addEventListener('change', () => {
  imagePreview();
  onEdit();
  if (!photo.validity.valid) photo.reportValidity();
});
byId('remove-photo').addEventListener('click', () => {
  photo.value = '';
  imagePreview();
  onEdit();
});

form.addEventListener('submit', async event => {
  event.preventDefault();
  if (saving || (savedCard && !changed)) return;
  if (!form.reportValidity()) return;
  formError.hidden = true;
  saving = true;
  save.firstChild.textContent = 'Сохраняем… ';
  displaySavedCard();

  const data = new FormData(form);
  const fieldsets = Array.from(form.querySelectorAll('fieldset'));
  for (const fieldset of fieldsets) fieldset.disabled = true;
  byId('consent').disabled = true;
  let failedField;
  try {
    const response = await fetch('/api/cards', {
      method: 'POST', headers: { 'X-ExtraMed': '1' }, body: data
    });
    const result = await response.json().catch(() => ({}));
    if (!response.ok) {
      failedField = result.field;
      if (response.status === 413) {
        failedField = 'photo';
        throw new Error(result.message || 'Выберите фото меньшего размера.');
      }
      throw new Error(result.message || (response.status === 429
        ? 'Слишком много попыток. Повторите позже.'
        : 'Не удалось сохранить карточку. Попробуйте ещё раз.'));
    }
    savedCard = result;
    changed = false;
    save.firstChild.textContent = 'Карточка создана ';
    if (!result.nfcUrl) {
      notice('Для записи на метку нужен HTTPS-адрес, доступный смартфону. Его задаёт администратор.');
    } else if (!nfcAvailable) {
      byId('manual').open = true;
      notice('Скопируйте ссылку для записи через отдельное NFC-приложение или откройте сайт в Chrome на Android.');
    } else {
      notice('Карточка сохранена. Можно записать метку.', 'success');
    }
  } catch (error) {
    formError.textContent = error instanceof TypeError ? 'Проверьте подключение и попробуйте ещё раз.' : error.message;
    formError.hidden = false;
    save.firstChild.textContent = 'Создать карточку ';
  } finally {
    saving = false;
    for (const fieldset of fieldsets) fieldset.disabled = false;
    byId('consent').disabled = false;
    displaySavedCard();
    if (formError.hidden) {
      byId('card-link').focus();
    } else {
      (byId(failedField) || formError).focus();
    }
  }
});

byId('copy-url').addEventListener('click', async () => {
  if (!savedCard?.nfcUrl || changed) return;
  try {
    await navigator.clipboard.writeText(savedCard.nfcUrl);
    notice('Ссылка скопирована.', 'success');
  } catch {
    const field = byId('nfc-url');
    field.focus();
    field.select();
    notice('Скопируйте выделенную ссылку.');
  }
});

write.addEventListener('click', async () => {
  if (!nfcAvailable || !savedCard?.nfcUrl || changed || writing) return;
  const url = savedCard.nfcUrl;
  writing = new AbortController();
  const timeout = setTimeout(() => writing?.abort('timeout'), 45_000);
  displaySavedCard();
  notice('Приложите телефон к нужной метке. Запись заменит её содержимое.');
  try {
    const reader = new NDEFReader();
    await reader.write({ records: [{ recordType: 'url', data: url }] }, { signal: writing.signal });
    notice('Метка записана. Считайте её смартфоном, чтобы проверить ссылку.', 'success');
  } catch (error) {
    if (!changed) {
      if (writing.signal.aborted) {
        notice(writing.signal.reason === 'timeout' ? 'Время ожидания истекло. Попробуйте снова.' : 'Запись отменена.');
      } else if (error.name === 'NotAllowedError') {
        notice('Разрешите доступ к NFC в браузере и повторите запись.', 'error');
      } else {
        notice('Запись не удалась. Включите NFC и приложите NDEF-метку, доступную для записи.', 'error');
      }
    }
  } finally {
    clearTimeout(timeout);
    writing = undefined;
    displaySavedCard();
  }
});

cancel.addEventListener('click', () => writing?.abort());

fetch('/api/settings').then(response => response.json()).then(settings => {
  byId('staff-availability').hidden = settings.staffEnabled;
  if (Number.isInteger(settings.photoLimitMb)) {
    photoLimitMb = settings.photoLimitMb;
    byId('photo-help').textContent = 'JPG, PNG или WebP до ' + photoLimitMb + ' МБ';
    imagePreview();
  }
}).catch(() => {});

imagePreview();
renderPreview();
