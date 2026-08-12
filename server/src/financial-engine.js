// Single source of truth for all money math in the app.
// Ported verbatim from the validated logic in the standalone calculator
// (حاسبة-الايجار-والمصاريف.html): integer "fils" arithmetic only, remainder
// distributed so that sum(rentShare) === rentFils and sum(expenseShare) === totalExpensesFils
// exactly, every time. This guarantees the settlement invariant:
//   sum(due_i) === rentFils   (proved algebraically, verified with automated tests)

function toFils(n) {
  return Math.round((Number(n) || 0) * 1000);
}

function fromFils(f) {
  return f / 1000;
}

function distributeFils(totalFils, n) {
  if (n <= 0) return [];
  const base = Math.trunc(totalFils / n);
  const remainder = totalFils - base * n;
  const out = new Array(n).fill(base);
  for (let i = 0; i < remainder; i++) out[i] += 1;
  return out;
}

/**
 * @param {number} rentFils
 * @param {Array<{userId:string}>} activeMembers - in stable order
 * @param {Array<{amountFils:number, paidByUserId:string}>} expenses
 * @returns {{
 *   n:number, totalExpensesFils:number, sumDue:number,
 *   byUserId: Record<string, {rentShareFils:number, expenseShareFils:number, paidFils:number, fairShareFils:number, dueFils:number}>
 * }}
 */
function computeSettlement(rentFils, activeMembers, expenses) {
  const n = activeMembers.length;
  const activeIds = new Set(activeMembers.map((m) => m.userId));

  const paidByUser = new Map();
  activeMembers.forEach((m) => paidByUser.set(m.userId, 0));
  let totalExpensesFils = 0;
  for (const exp of expenses) {
    if (!activeIds.has(exp.paidByUserId)) continue; // paid by someone no longer active this period
    paidByUser.set(exp.paidByUserId, (paidByUser.get(exp.paidByUserId) || 0) + exp.amountFils);
    totalExpensesFils += exp.amountFils;
  }

  const rentShares = distributeFils(rentFils, n);
  const expenseShares = distributeFils(totalExpensesFils, n);

  const byUserId = {};
  let sumDue = 0;
  activeMembers.forEach((m, i) => {
    const paidFils = paidByUser.get(m.userId) || 0;
    const fairShareFils = rentShares[i] + expenseShares[i];
    const dueFils = fairShareFils - paidFils;
    sumDue += dueFils;
    byUserId[m.userId] = {
      rentShareFils: rentShares[i],
      expenseShareFils: expenseShares[i],
      paidFils,
      fairShareFils,
      dueFils
    };
  });

  return { n, totalExpensesFils, sumDue, byUserId };
}

function statusOf(dueFils) {
  if (dueFils > 0) return "pay"; // عليه
  if (dueFils < 0) return "get"; // له
  return "even"; // متعادل
}

function fmtMoney(fils) {
  const v = fromFils(fils);
  return v.toLocaleString("en-US", { minimumFractionDigits: 3, maximumFractionDigits: 3 }) + " د.ك";
}

module.exports = { toFils, fromFils, distributeFils, computeSettlement, statusOf, fmtMoney };
