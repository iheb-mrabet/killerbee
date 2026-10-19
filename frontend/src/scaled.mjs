// Work only with integer cents/centigrams. Do not round binary floating-point input.
export function parseScaled(value) {
  const normalized = String(value).trim().replace(',', '.');
  if (!/^\d{1,9}(\.\d{1,2})?$/.test(normalized)) throw new Error('Saisissez un nombre positif avec au maximum deux décimales.');
  const [whole, fraction = ''] = normalized.split('.');
  const result = BigInt(whole) * 100n + BigInt(fraction.padEnd(2, '0'));
  if (result > 100000000n) throw new Error('La valeur dépasse la limite autorisée.');
  return Number(result);
}
export function inputScaled(value) {
  return `${Math.trunc(value / 100)}.${String(value % 100).padStart(2, '0')}`;
}
export function displayScaled(value) {
  return `${Math.trunc(value / 100).toLocaleString('fr-FR')},${String(value % 100).padStart(2, '0')}`;
}
