const crypto = require("crypto");
const db = require("../db");

let ioInstance = null;

function init(io) {
  ioInstance = io;
}

/** Broadcasts a realtime event to everyone currently viewing this group. */
function emitToGroup(groupId, eventName, payload) {
  if (ioInstance) ioInstance.to(`group:${groupId}`).emit(eventName, payload);
}

/**
 * Persists one notification row per affected user and pushes it live to any
 * connected session of theirs. This is the single choke point every route
 * must go through to notify someone — see requirement #36/#37 (central event
 * system, recipients resolved by role/group/impact, not "notify everyone").
 */
async function notifyUsers(groupId, userIds, { type, title, message, relatedEntityType, relatedEntityId, priority }) {
  const uniqueIds = [...new Set(userIds)];
  for (const userId of uniqueIds) {
    const id = crypto.randomUUID();
    await db.prepare(
      `INSERT INTO notifications (id, group_id, user_id, type, title, message, related_entity_type, related_entity_id, priority)
       VALUES (?, ?, ?, ?, ?, ?, ?, ?, ?)`
    ).run(
      id,
      groupId,
      userId,
      type,
      title,
      message,
      relatedEntityType || null,
      relatedEntityId || null,
      priority || "normal"
    );
    const row = await db.prepare("SELECT * FROM notifications WHERE id = ?").get(id);
    if (ioInstance) ioInstance.to(`user:${userId}`).emit("notification", row);
  }
}

async function writeAudit(groupId, actorUserId, action, entityType, entityId, oldValue, newValue) {
  await db.prepare(
    `INSERT INTO audit_log (id, group_id, actor_user_id, action, entity_type, entity_id, old_value, new_value)
     VALUES (?, ?, ?, ?, ?, ?, ?, ?)`
  ).run(
    crypto.randomUUID(),
    groupId,
    actorUserId,
    action,
    entityType || null,
    entityId || null,
    oldValue != null ? JSON.stringify(oldValue) : null,
    newValue != null ? JSON.stringify(newValue) : null
  );
}

module.exports = { init, emitToGroup, notifyUsers, writeAudit };
