// Shared bits for showing estimated grocery costs
export function money(n: number | null | undefined) {
  return n == null ? '—' : n.toLocaleString(undefined, { style: 'currency', currency: 'USD' });
}

export function budgetUse(total: number | null, budget: number | null) {
  if (total == null || budget == null || budget <= 0) return null;
  const percent = Math.round(total / budget * 100);
  const left = Math.round((budget - total) * 100) / 100;
  return { percent, left, over: left < 0 };
}
