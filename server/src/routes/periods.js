const express = require("express");
const crypto = require("crypto");
const db = require("../db");
const { requireAuth, loadMembership, requireRole } = require("../auth/middleware");
const { getOpenPeriod, getGroupOr404, getAllMemberships } = require("../services/settlement-service");
const { emitToGroup, notifyUsers, writeAudit } = require("../services/events");
const asyncHandler = require("../utils/async-handler");

const router = express.Router({ mergeParams: true });

function monthLabel(date = new Date()) {
  return date.toLocaleDateString("ar-KW-u-nu-latn", { year: "numeric", month: "long" });
}

router.use(requireAuth, loadMembership);

router.get("/history", asyncHandler(async (req, res) => {
  const periods = await db
    .prepare("SELECT * FROM financial_periods WHERE group_id = ? ORDER BY opened_at DESC")
    .all(req.params.groupId);
  res.json({ periods });
}));

router.use(requireRole("admin"));

router.post("/new-month", asyncHandler(async (req, res) => {
  const openPeriod = await getOpenPeriod(req.params.groupId);
  if (openPeriod) {
    await db.prepare("UPDATE financial_periods SET status = 'closed', closed_at = now() WHERE id = ?").run(openPeriod.id);
    await writeAudit(req.params.groupId, req.user.id, "period_closed", "financial_period", openPeriod.id, { status: "open" }, { status: "closed" });
  }

  const group = await getGroupOr404(req.params.groupId);
  const label = String(req.body.label || monthLabel()).trim();
  const newPeriodId = crypto.randomUUID();
  await db.prepare("INSERT INTO financial_periods (id, group_id, label, rent_fils_snapshot) VALUES (?, ?, ?, ?)").run(
    newPeriodId,
    req.params.groupId,
    label,
    group.rent_fils
  );
  await writeAudit(req.params.groupId, req.user.id, "period_opened", "financial_period", newPeriodId, null, { label });

  const allM = await getAllMemberships(req.params.groupId);
  const recipients = allM.filter((m) => m.status === "active").map((m) => m.user_id);
  await notifyUsers(req.params.groupId, recipients, {
    type: "new_month_started",
    title: "بدأ شهر جديد",
    message: `تم بدء حساب: ${label}، مع الاحتفاظ بالشهر السابق في السجل`
  });
  emitToGroup(req.params.groupId, "NewMonthStarted", { periodId: newPeriodId });
  emitToGroup(req.params.groupId, "BalanceChanged", {});
  res.status(201).json({ period: await db.prepare("SELECT * FROM financial_periods WHERE id = ?").get(newPeriodId) });
}));

module.exports = router;
