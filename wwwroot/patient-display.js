const sexes = { female: 'Женский', male: 'Мужской', unknown: 'Пол не указан' };
const pregnancies = { yes: 'Да', no: 'Нет', unknown: 'Не указано', na: 'Неприменимо' };

export function patientMeta(sex, age) {
  const details = [];
  if (sexes[sex]) details.push(sexes[sex]);
  if (age !== '' && Number.isInteger(Number(age))) {
    const count = Number(age);
    const last = count % 10;
    const tens = count % 100;
    const word = tens >= 11 && tens <= 14 ? 'лет' : last === 1 ? 'год' : last >= 2 && last <= 4 ? 'года' : 'лет';
    details.push(count + ' ' + word);
  }
  return details.join(' · ');
}

export function pregnancyText(value, details) {
  return value === 'yes' && details?.trim() ? 'Да · ' + details.trim() : pregnancies[value] || 'Не указано';
}
