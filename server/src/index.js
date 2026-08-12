const express = require("express");
const cors = require("cors");
const http = require("http");
const path = require("path");
const { Server } = require("socket.io");
const db = require("./db");

const app = express();
app.use(cors());
app.use(express.json());

app.get("/health", (req, res) => res.json({ ok: true, time: new Date().toISOString() }));

app.use(express.static(path.join(__dirname, "..", "..", "web")));

app.use("/auth", require("./routes/auth"));
app.use("/groups", require("./routes/groups"));
app.use("/groups/:groupId/expenses", require("./routes/expenses"));
app.use("/groups/:groupId/period", require("./routes/periods"));
app.use("/notifications", require("./routes/notifications"));

app.use((err, req, res, next) => {
  console.error(err);
  res.status(500).json({ error: "خطأ غير متوقع في السيرفر" });
});

const server = http.createServer(app);
const io = new Server(server, { cors: { origin: "*" } });
require("./services/events").init(io);
require("./sockets").setupSockets(io);

const PORT = process.env.PORT || 4310;

db.initSchema()
  .then(() => {
    server.listen(PORT, "0.0.0.0", () => {
      console.log(`Rent App server listening on http://0.0.0.0:${PORT}`);
    });
  })
  .catch((err) => {
    console.error("Failed to initialize database schema:", err);
    process.exit(1);
  });
