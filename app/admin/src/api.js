const TOKEN_KEY = 'clickfun-admin-token'

export function getToken() {
  return localStorage.getItem(TOKEN_KEY) || ''
}

export function setToken(token) {
  localStorage.setItem(TOKEN_KEY, token)
}

export function clearToken() {
  localStorage.removeItem(TOKEN_KEY)
}

async function request(method, path, body, isForm = false) {
  const headers = {}
  const token = getToken()
  if (token) headers['X-ClickFun-Token'] = token
  if (!isForm && body !== undefined) headers['Content-Type'] = 'application/json'

  const res = await fetch(path, {
    method,
    headers,
    body: body === undefined ? undefined : isForm ? body : JSON.stringify(body),
  })

  if (res.status === 401) {
    clearToken()
    window.dispatchEvent(new CustomEvent('clickfun-admin:unauthorized'))
    throw new Error('401 — sai hoặc thiếu bridge token')
  }

  const text = await res.text()
  let data = null
  try { data = text ? JSON.parse(text) : null } catch { data = text }

  if (!res.ok) {
    const msg = (data && data.error) || (data && data.message) || `HTTP ${res.status}`
    throw new Error(msg)
  }
  return data
}

export const api = {
  health: () => request('GET', '/health'),
  settings: () => request('GET', '/settings'),
  saveSettings: (payload) => request('PUT', '/settings', payload),
  cameraStatus: () => request('GET', '/camera/status'),
  cameraConnect: () => request('POST', '/camera/connect', {}),
  cameraDisconnect: () => request('POST', '/camera/disconnect', {}),
  cameraCapture: () => request('POST', '/capture', {}),
  printLayouts: () => request('GET', '/print/layouts'),

  adminConfig: () => request('GET', '/admin/config'),
  adminStats: () => request('GET', '/admin/stats'),
  listFrames: () => request('GET', '/admin/frames'),
  createFrame: (formData) => request('POST', '/admin/frames', formData, true),
  updateFrame: (id, payload) => request('PUT', `/admin/frames/${id}`, payload),
  deleteFrame: (id) => request('DELETE', `/admin/frames/${id}`),
  listLayouts: () => request('GET', '/admin/layouts'),
  createLayout: (payload) => request('POST', '/admin/layouts', payload),
  updateLayout: (id, payload) => request('PUT', `/admin/layouts/${id}`, payload),
  deleteLayout: (id) => request('DELETE', `/admin/layouts/${id}`),
  listCategories: () => request('GET', '/admin/categories'),
  createCategory: (name) => request('POST', '/admin/categories', { name }),
  deleteCategory: (id) => request('DELETE', `/admin/categories/${id}`),
  prints: (tail = 100) => request('GET', `/admin/prints?tail=${tail}`),
  listSessions: () => request('GET', '/admin/sessions'),
  listSessionPhotos: (date) => request('GET', `/admin/sessions/${date}`),
  photoUrl: (date, file) => `/admin/photo/${encodeURIComponent(date)}/${encodeURIComponent(file)}`,
  deletePhoto: (date, file) => request('DELETE', `/admin/photo/${encodeURIComponent(date)}/${encodeURIComponent(file)}`),
  filters: () => request('GET', '/admin/filters'),
  filterPreviewUrl: (date, file, filter) => `/admin/filter-preview/${encodeURIComponent(date)}/${encodeURIComponent(file)}?filter=${encodeURIComponent(filter || 'none')}`,
  logs: (tail = 150) => request('GET', `/admin/logs?tail=${tail}`),
}
