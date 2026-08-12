// Thin fetch wrapper: owns token storage + silent refresh. Every screen calls
// through this instead of touching fetch/localStorage directly.
const Api = (() => {
  const BASE = ""; // same-origin: server serves this app and the API together

  function getTokens() {
    return {
      accessToken: localStorage.getItem("accessToken"),
      refreshToken: localStorage.getItem("refreshToken")
    };
  }
  function setTokens(accessToken, refreshToken) {
    if (accessToken) localStorage.setItem("accessToken", accessToken);
    if (refreshToken) localStorage.setItem("refreshToken", refreshToken);
  }
  function clearTokens() {
    localStorage.removeItem("accessToken");
    localStorage.removeItem("refreshToken");
    localStorage.removeItem("me");
    localStorage.removeItem("currentGroupId");
  }
  function getMe() {
    const raw = localStorage.getItem("me");
    return raw ? JSON.parse(raw) : null;
  }
  function setMe(user) {
    localStorage.setItem("me", JSON.stringify(user));
  }

  async function refreshAccessToken() {
    const { refreshToken } = getTokens();
    if (!refreshToken) return false;
    const res = await fetch(BASE + "/auth/refresh", {
      method: "POST",
      headers: { "Content-Type": "application/json" },
      body: JSON.stringify({ refreshToken })
    });
    if (!res.ok) return false;
    const data = await res.json();
    setTokens(data.accessToken, data.refreshToken);
    return true;
  }

  async function request(method, path, body, opts) {
    opts = opts || {};
    const { accessToken } = getTokens();
    const headers = { "Content-Type": "application/json" };
    if (accessToken && !opts.noAuth) headers.Authorization = "Bearer " + accessToken;

    let res;
    try {
      res = await fetch(BASE + path, {
        method,
        headers,
        body: body !== undefined ? JSON.stringify(body) : undefined
      });
    } catch (e) {
      const err = new Error("لا يوجد اتصال بالسيرفر");
      err.offline = true;
      throw err;
    }

    if (res.status === 401 && !opts.noAuth && !opts._retried) {
      const refreshed = await refreshAccessToken();
      if (refreshed) return request(method, path, body, { ...opts, _retried: true });
      clearTokens();
      window.location.hash = "#/login";
    }

    const contentType = res.headers.get("content-type") || "";
    const data = contentType.includes("application/json") ? await res.json() : null;
    if (!res.ok) {
      const err = new Error((data && data.error) || "حدث خطأ");
      err.status = res.status;
      err.data = data;
      throw err;
    }
    return data;
  }

  return {
    getTokens, setTokens, clearTokens, getMe, setMe,
    get: (path) => request("GET", path),
    post: (path, body, opts) => request("POST", path, body, opts),
    patch: (path, body) => request("PATCH", path, body),
    del: (path) => request("DELETE", path)
  };
})();
