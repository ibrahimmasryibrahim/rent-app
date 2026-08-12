const db = require("../db");
const { computeSettlement } = require("../financial-engine");

async function getGroupOr404(groupId) {
  return db.prepare("SELECT * FROM groups WHERE id = ?").get(groupId);
}

async function getOpenPeriod(groupId) {
  return db
    .prepare("SELECT * FROM financial_periods WHERE group_id = ? AND status = 'open' ORDER BY opened_at DESC LIMIT 1")
    .get(groupId);
}

async function getAllMemberships(groupId) {
  return db
    .prepare(
      `SELECT m.*, u.name as user_name, u.phone as user_phone
       FROM memberships m JOIN users u ON u.id = m.user_id
       WHERE m.group_id = ? ORDER BY m.sort_order ASC, m.joined_at ASC`
    )
    .all(groupId);
}

async function getActiveMemberships(groupId) {
  const all = await getAllMemberships(groupId);
  return all.filter((m) => m.status === "active");
}

async function getExpensesForPeriod(periodId) {
  return db
    .prepare(
      `SELECT e.*, u.name as paid_by_name, c.name as created_by_name
       FROM expenses e
       JOIN users u ON u.id = e.paid_by_user_id
       JOIN users c ON c.id = e.created_by_user_id
       WHERE e.period_id = ? ORDER BY e.created_at DESC`
    )
    .all(periodId);
}

/**
 * Full settlement snapshot for a group's currently-open period.
 * Returns null period if the group somehow has no open period yet.
 */
async function computeGroupSettlement(groupId) {
  const group = await getGroupOr404(groupId);
  const period = await getOpenPeriod(groupId);
  const activeMembers = await getActiveMemberships(groupId);
  if (!period) {
    return { group, period: null, activeMembers, expenses: [], settlement: null };
  }
  const expenses = await getExpensesForPeriod(period.id);
  const settlement = computeSettlement(
    period.rent_fils_snapshot,
    activeMembers.map((m) => ({ userId: m.user_id })),
    expenses.map((e) => ({ amountFils: e.amount_fils, paidByUserId: e.paid_by_user_id }))
  );
  return { group, period, activeMembers, expenses, settlement };
}

module.exports = {
  getGroupOr404,
  getOpenPeriod,
  getAllMemberships,
  getActiveMemberships,
  getExpensesForPeriod,
  computeGroupSettlement
};
