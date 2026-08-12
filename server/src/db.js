const path = require("path");
const fs = require("fs");
const { Pool } = require("pg");

const connectionString = process.env.DATABASE_URL;
if (!connectionString) {
  console.error("DATABASE_URL is not set. Create a free Postgres database (e.g. on Neon) and set DATABASE_URL.");
  process.exit(1);
}

const pool = new Pool({
  connectionString,
  ssl: connectionString.includes("sslmode=require") || process.env.NODE_ENV === "production"
    ? { rejectUnauthorized: false }
    : false
});

// Mimics the synchronous better-sqlite3 / node:sqlite `db.prepare(sql).get/.all/.run(...)`
// shape so the rest of the codebase barely changes — callers just add `await`.
// `?` placeholders are converted to Postgres's `$1, $2, ...` automatically.
function toPgPlaceholders(sql) {
  let i = 0;
  return sql.replace(/\?/g, () => `$${++i}`);
}

function prepare(sql) {
  const pgSql = toPgPlaceholders(sql);
  return {
    async get(...params) {
      const res = await pool.query(pgSql, params);
      return res.rows[0];
    },
    async all(...params) {
      const res = await pool.query(pgSql, params);
      return res.rows;
    },
    async run(...params) {
      const res = await pool.query(pgSql, params);
      return { changes: res.rowCount };
    }
  };
}

async function initSchema() {
  const schema = fs.readFileSync(path.join(__dirname, "schema.sql"), "utf8");
  await pool.query(schema);
}

module.exports = { prepare, pool, initSchema };
