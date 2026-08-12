const db = require("../db");
const { verifyAccessToken } = require("./jwt");
const asyncHandler = require("../utils/async-handler");

// Every protected route goes through requireAuth first, which trusts nothing
// from the client except the signed JWT. requireGroupRole then re-checks the
// membership row in the database on every single request — permissions are
// never inferred from anything the client sends or from hiding a button.

async function requireAuth(req, res, next) {
  const header = req.headers.authorization || "";
  const token = header.startsWith("Bearer ") ? header.slice(7) : null;
  if (!token) return res.status(401).json({ error: "غير مصرح — الرجاء تسجيل الدخول" });
  const userId = verifyAccessToken(token);
  if (!userId) return res.status(401).json({ error: "الجلسة منتهية، الرجاء تسجيل الدخول مجددًا" });
  const user = await db.prepare("SELECT * FROM users WHERE id = ?").get(userId);
  if (!user) return res.status(401).json({ error: "المستخدم غير موجود" });
  req.user = user;
  next();
}

async function loadMembership(req, res, next) {
  const groupId = req.params.groupId || req.params.id;
  const membership = await db
    .prepare("SELECT * FROM memberships WHERE group_id = ? AND user_id = ?")
    .get(groupId, req.user.id);
  if (!membership) return res.status(403).json({ error: "لست عضوًا في هذه المجموعة" });
  req.membership = membership;
  next();
}

function requireRole(role) {
  return (req, res, next) => {
    if (!req.membership) return res.status(500).json({ error: "loadMembership must run before requireRole" });
    if (req.membership.role !== role) {
      return res.status(403).json({ error: "لا تملك صلاحية تنفيذ هذا الإجراء" });
    }
    next();
  };
}

function requireActiveMembership(req, res, next) {
  if (req.membership.status !== "active") {
    return res.status(403).json({ error: "حسابك معلّق في هذه المجموعة حاليًا" });
  }
  next();
}

module.exports = {
  requireAuth: asyncHandler(requireAuth),
  loadMembership: asyncHandler(loadMembership),
  requireRole,
  requireActiveMembership
};
