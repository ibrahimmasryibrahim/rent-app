const db = require("./db");
const { verifyAccessToken } = require("./auth/jwt");

function setupSockets(io) {
  io.use((socket, next) => {
    const token = socket.handshake.auth && socket.handshake.auth.token;
    const userId = token ? verifyAccessToken(token) : null;
    if (!userId) return next(new Error("unauthorized"));
    socket.userId = userId;
    next();
  });

  io.on("connection", (socket) => {
    socket.join(`user:${socket.userId}`);

    socket.on("join-group", async (groupId) => {
      const membership = await db
        .prepare("SELECT * FROM memberships WHERE group_id = ? AND user_id = ?")
        .get(groupId, socket.userId);
      if (!membership) return; // silently ignore — server never trusts the client's claim of membership
      socket.join(`group:${groupId}`);
    });

    socket.on("leave-group", (groupId) => {
      socket.leave(`group:${groupId}`);
    });
  });
}

module.exports = { setupSockets };
