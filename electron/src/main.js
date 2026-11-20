'use strict';
// Claude Multi-Account Launcher (cross-platform edition, built on Electron)
// By Gerizon Technologies. Open source (MIT). Not affiliated with Anthropic.
//
// Each account lives in its own tab. Every tab uses its own private browser
// storage ("partition"), so each one stays signed in to a different Claude
// account. Tabs, their order and the window position are saved in
// accounts.json inside the app's data folder.

const {
  app, BrowserWindow, WebContentsView, Menu, ipcMain, dialog, shell,
  session, clipboard, nativeImage, screen,
} = require('electron');
const path = require('path');
const fs = require('fs');
const crypto = require('crypto');

const BRAND = {
  name: 'Claude Multi-Account Launcher',
  company: 'Gerizon Technologies',
  legal: 'Gerizon Information Technology Solutions',
  repo: 'https://github.com/nelzon-gerizontech/claude-multi-account-launcher',
  disclaimer: 'Claude is a trademark of Anthropic. This app is an independent ' +
    'open-source tool and is not affiliated with or endorsed by Anthropic.',
};

const IS_MAC = process.platform === 'darwin';
const BAR = 48; // height of the tab bar, in pixels
const START_URL = process.env.CMAL_START_URL || 'https://claude.ai/new';
const SELFTEST = process.env.CMAL_SELFTEST || ''; // '1' or '2' (test phases)
const CLAUDE_HOSTS = ['claude.ai', 'claude.com', 'anthropic.com'];
const SIGNIN_HOSTS = CLAUDE_HOSTS.concat(['accounts.google.com', 'appleid.apple.com']);
const ALLOWED_PERMISSIONS = new Set([
  'media', 'clipboard-read', 'clipboard-sanitized-write', 'notifications',
  'fullscreen', 'pointerLock',
]);

if (process.env.CMAL_USER_DATA) app.setPath('userData', process.env.CMAL_USER_DATA);

// ---------------------------------------------------------------- helpers

function httpsHost(url) {
  try {
    const u = new URL(url);
    return u.protocol === 'https:' ? u.hostname.toLowerCase() : null;
  } catch { return null; }
}
function hostIn(host, list) {
  return !!host && list.some(h => host === h || host.endsWith('.' + h));
}
function log(...args) { if (SELFTEST || !app.isPackaged) console.log('[cmal]', ...args); }

// ---------------------------------------------------------------- storage

const dataDir = () => app.getPath('userData');
const storeFile = () => path.join(dataDir(), 'accounts.json');
const partitionName = id => 'persist:acct-' + id;
const partitionDir = id => path.join(dataDir(), 'Partitions', 'acct-' + id);

let store = { accounts: [], activeId: null, pendingDelete: [], bounds: null, maximized: false };

function loadStore() {
  try {
    const s = JSON.parse(fs.readFileSync(storeFile(), 'utf8'));
    store = Object.assign(store, s);
  } catch { /* first run, or unreadable file: start empty */ }
  store.accounts = (Array.isArray(store.accounts) ? store.accounts : [])
    .filter(a => a && /^[a-f0-9]{12}$/.test(a.id) && typeof a.name === 'string')
    .map(a => ({ id: a.id, name: a.name.slice(0, 40), color: (a.color | 0) % 8, signedIn: !!a.signedIn }));
  store.pendingDelete = (Array.isArray(store.pendingDelete) ? store.pendingDelete : [])
    .filter(id => /^[a-f0-9]{12}$/.test(id));
  if (!store.accounts.some(a => a.id === store.activeId)) {
    store.activeId = store.accounts.length ? store.accounts[0].id : null;
  }
}

function saveStore() {
  try {
    fs.mkdirSync(dataDir(), { recursive: true });
    const tmp = storeFile() + '.tmp';
    fs.writeFileSync(tmp, JSON.stringify(store, null, 2));
    fs.renameSync(tmp, storeFile());
  } catch (e) { log('save failed', e.message); }
}

// Removes the browser data of deleted accounts. While the app runs, a deleted
// account's folder can be written to again as its page shuts down, so on start
// every Partitions folder that no longer belongs to an account is removed.
function purgeDeleted(atStartup) {
  const keep = new Set(store.accounts.map(a => 'acct-' + a.id));
  const root = path.join(dataDir(), 'Partitions');
  let names = [];
  try { names = fs.readdirSync(root); } catch { /* nothing yet */ }
  for (const name of names) {
    if (!/^acct-[a-f0-9]{12}$/.test(name) || keep.has(name)) continue;
    const id = name.slice(5);
    if (!atStartup && !store.pendingDelete.includes(id)) continue;
    try { fs.rmSync(path.join(root, name), { recursive: true, force: true }); } catch { /* retry next start */ }
  }
  if (atStartup) {
    store.pendingDelete = store.pendingDelete.filter(id => fs.existsSync(partitionDir(id)));
  }
  saveStore();
}

// ---------------------------------------------------------------- state

let win = null;
const views = new Map(); // account id -> WebContentsView
const preparedSessions = new WeakSet();
let overlay = false; // true while a dialog in the tab bar window covers the page

const findAccount = id => store.accounts.find(a => a.id === id);
const activeAccount = () => findAccount(store.activeId);

function pushState() {
  if (!win || win.isDestroyed()) return;
  win.webContents.send('state', {
    accounts: store.accounts,
    activeId: store.activeId,
    platform: process.platform,
  });
  buildAppMenu();
}

function layout() {
  if (!win || win.isDestroyed()) return;
  const [w, h] = win.getContentSize();
  for (const [id, view] of views) {
    const show = id === store.activeId && !overlay;
    view.setVisible(show);
    if (show) view.setBounds({ x: 0, y: BAR, width: w, height: Math.max(0, h - BAR) });
  }
}

// ---------------------------------------------------------------- pages

function prepareSession(ses) {
  if (preparedSessions.has(ses)) return;
  preparedSessions.add(ses);
  // Sign-in pages (Google in particular) refuse browsers that announce
  // themselves as "Electron", so present a plain Chrome user agent.
  // Keep only the standard browser parts (Mozilla, AppleWebKit, Chrome, Safari).
  const ua = ses.getUserAgent()
    .replace(/\s(?!(?:Mozilla|AppleWebKit|Chrome|Safari|Version)\/)[^\s()\/]+\/[\d.]+/g, '');
  ses.setUserAgent(ua);
  // Only Claude's own sites may use the microphone, clipboard or notifications.
  ses.setPermissionRequestHandler((wc, permission, callback, details) => {
    const host = httpsHost(details.requestingUrl || wc.getURL());
    callback(ALLOWED_PERMISSIONS.has(permission) && hostIn(host, CLAUDE_HOSTS));
  });
  ses.setPermissionCheckHandler((wc, permission, origin) =>
    ALLOWED_PERMISSIONS.has(permission) && hostIn(httpsHost(origin), CLAUDE_HOSTS));
}

function updateSignedIn(id, url) {
  const acct = findAccount(id);
  if (!acct) return;
  const host = httpsHost(url);
  let signedIn = false;
  if (hostIn(host, ['claude.ai'])) {
    const p = new URL(url).pathname.toLowerCase();
    signedIn = !/^\/(login|logout|signup|magic-link|onboarding|verify)/.test(p);
  }
  if (acct.signedIn !== signedIn) {
    acct.signedIn = signedIn;
    saveStore();
    pushState();
  }
}

function getView(acct) {
  let view = views.get(acct.id);
  if (view) return view;
  const part = partitionName(acct.id);
  prepareSession(session.fromPartition(part));
  view = new WebContentsView({
    webPreferences: { partition: part, contextIsolation: true, sandbox: true, nodeIntegration: false },
  });
  view.setBackgroundColor('#FFFFFF');
  const wc = view.webContents;
  wc.setWindowOpenHandler(({ url }) => {
    // Sign-in pop-ups stay inside the app so they share this tab's sign-in.
    if (hostIn(httpsHost(url), SIGNIN_HOSTS)) {
      return {
        action: 'allow',
        overrideBrowserWindowOptions: {
          width: 520, height: 720, parent: win, autoHideMenuBar: true,
          title: BRAND.name, backgroundColor: '#FFFFFF',
        },
      };
    }
    // Everything else (shared links, docs, downloads pages) opens in the normal browser.
    if (/^https?:\/\//i.test(url)) shell.openExternal(url);
    return { action: 'deny' };
  });
  const refresh = () => updateSignedIn(acct.id, wc.getURL());
  wc.on('did-navigate', refresh);
  wc.on('did-navigate-in-page', refresh);
  wc.on('did-fail-load', (e, code, desc, url, isMain) => {
    if (isMain && code !== -3) log('load failed', acct.id, code, desc, url);
  });
  wc.on('render-process-gone', (e, details) => log('page crashed', acct.id, details.reason));
  win.contentView.addChildView(view);
  views.set(acct.id, view);
  wc.loadURL(START_URL);
  return view;
}

// ---------------------------------------------------------------- actions

function selectAccount(id) {
  const acct = findAccount(id);
  if (!acct) return;
  store.activeId = id;
  const view = getView(acct);
  layout();
  if (!overlay) view.webContents.focus();
  saveStore();
  pushState();
}

function addAccount(name) {
  name = String(name || '').trim().slice(0, 40);
  if (!name) return null;
  const used = new Set(store.accounts.map(a => a.color));
  let color = store.accounts.length % 8;
  for (let i = 0; i < 8; i++) if (!used.has(i)) { color = i; break; }
  const acct = { id: crypto.randomBytes(6).toString('hex'), name, color, signedIn: false };
  store.accounts.push(acct);
  selectAccount(acct.id);
  return acct;
}

function renameAccount(id, name) {
  const acct = findAccount(id);
  name = String(name || '').trim().slice(0, 40);
  if (!acct || !name) return;
  acct.name = name;
  saveStore();
  pushState();
}

function moveAccount(id, delta) {
  const i = store.accounts.findIndex(a => a.id === id);
  const j = i + delta;
  if (i < 0 || j < 0 || j >= store.accounts.length) return;
  const [acct] = store.accounts.splice(i, 1);
  store.accounts.splice(j, 0, acct);
  saveStore();
  pushState();
}

async function deleteAccount(id, skipConfirm) {
  const acct = findAccount(id);
  if (!acct) return false;
  if (!skipConfirm) {
    const { response } = await dialog.showMessageBox(win, {
      type: 'warning',
      buttons: ['Delete account', 'Cancel'],
      defaultId: 1,
      cancelId: 1,
      message: `Delete "${acct.name}"?`,
      detail: 'This removes the tab and signs this account out on this computer. ' +
        'Its saved sign-in and browser data here are deleted.\n\n' +
        'Your Claude account itself, and its chats, are not affected. ' +
        'You can add it again later and sign in.',
    });
    if (response !== 0) return false;
  }
  const view = views.get(id);
  if (view) {
    win.contentView.removeChildView(view);
    view.webContents.close();
    views.delete(id);
  }
  const index = store.accounts.indexOf(acct);
  store.accounts.splice(index, 1);
  if (store.activeId === id) {
    const next = store.accounts[Math.min(index, store.accounts.length - 1)];
    store.activeId = next ? next.id : null;
  }
  store.pendingDelete.push(id);
  saveStore();
  try {
    const ses = session.fromPartition(partitionName(id));
    await ses.clearStorageData();
    await ses.clearCache();
  } catch (e) { log('clear failed', e.message); }
  purgeDeleted();
  if (store.activeId) selectAccount(store.activeId); else { layout(); pushState(); }
  return true;
}

function reloadActive(ignoreCache) {
  const view = views.get(store.activeId);
  if (!view) return;
  if (ignoreCache) view.webContents.reloadIgnoringCache(); else view.webContents.reload();
}

function cycle(delta) {
  const n = store.accounts.length;
  if (n < 2) return;
  const i = store.accounts.findIndex(a => a.id === store.activeId);
  selectAccount(store.accounts[(i + delta + n) % n].id);
}

// Sign-in emails open in the normal browser. This lets people copy that link
// and open it inside the right tab instead.
async function pasteSignInLink(id) {
  const acct = findAccount(id || store.activeId);
  if (!acct) return;
  const text = clipboard.readText().trim();
  if (hostIn(httpsHost(text), CLAUDE_HOSTS)) {
    selectAccount(acct.id);
    getView(acct).webContents.loadURL(text);
    return;
  }
  await dialog.showMessageBox(win, {
    type: 'info',
    buttons: ['OK'],
    message: 'No Claude sign-in link found',
    detail: 'Open the sign-in email from Claude, right-click the sign-in button and copy ' +
      'the link, then choose "Paste sign-in link" again.\n\nOnly links to claude.ai, ' +
      'claude.com or anthropic.com are accepted.',
  });
}

function promptRename(id) {
  const acct = findAccount(id);
  if (acct) win.webContents.send('prompt', { mode: 'rename', id, name: acct.name });
}
function promptAdd() { win.webContents.send('prompt', { mode: 'add' }); }

function logoImage(size) {
  const img = nativeImage.createFromPath(path.join(__dirname, 'mark.png'));
  return img.isEmpty() ? img : img.resize({ width: size, height: size });
}

async function showAbout() {
  const { response } = await dialog.showMessageBox(win, {
    type: 'none',
    icon: logoImage(72),
    buttons: ['OK', 'Open GitHub page'],
    defaultId: 0,
    message: BRAND.name,
    detail: `Version ${app.getVersion()}\nA product of ${BRAND.company}\n` +
      `© 2026 ${BRAND.legal}\nOpen source under the MIT license.\n\n${BRAND.disclaimer}`,
  });
  if (response === 1) shell.openExternal(BRAND.repo);
}

async function showHelp() {
  const mod = IS_MAC ? '⌘' : 'Ctrl+';
  await dialog.showMessageBox(win, {
    type: 'info',
    buttons: ['OK'],
    message: 'How to use ' + BRAND.name,
    detail:
      '• Click + to add an account, then sign in inside its tab.\n' +
      '• Each tab keeps its own sign-in, even after you quit or restart.\n' +
      '• Right-click a tab to rename, move, reload or delete it.\n' +
      '• An orange dot means that tab is not signed in yet.\n\n' +
      'If Google sign-in is refused, sign in with your email instead. When the ' +
      'sign-in email arrives, copy its link and choose "Paste sign-in link".\n\n' +
      `Shortcuts: ${mod}T add account · ${mod}1 to ${mod}9 jump to a tab · ` +
      `Ctrl+Tab next tab · ${mod}R reload`,
  });
}

// ---------------------------------------------------------------- menus

function accountMenuItems(id) {
  const acct = findAccount(id);
  if (!acct) return [];
  const i = store.accounts.indexOf(acct);
  return [
    { label: 'Rename…', click: () => promptRename(id) },
    { label: 'Move left', enabled: i > 0, click: () => moveAccount(id, -1) },
    { label: 'Move right', enabled: i < store.accounts.length - 1, click: () => moveAccount(id, 1) },
    { type: 'separator' },
    { label: 'Reload', click: () => { selectAccount(id); reloadActive(false); } },
    { label: 'Paste sign-in link', click: () => pasteSignInLink(id) },
    { type: 'separator' },
    { label: 'Delete account…', click: () => deleteAccount(id) },
  ];
}

function popup(template) {
  Menu.buildFromTemplate(template).popup({ window: win });
}

function buildAppMenu() {
  const accounts = store.accounts.map((a, i) => ({
    label: a.name,
    type: 'radio',
    checked: a.id === store.activeId,
    accelerator: i < 9 ? 'CmdOrCtrl+' + (i + 1) : undefined,
    click: () => selectAccount(a.id),
  }));
  const hasActive = !!activeAccount();
  const template = [
    ...(IS_MAC ? [{
      label: BRAND.name,
      submenu: [
        { label: 'About ' + BRAND.name, click: showAbout },
        { type: 'separator' },
        { role: 'services' },
        { type: 'separator' },
        { role: 'hide' }, { role: 'hideOthers' }, { role: 'unhide' },
        { type: 'separator' },
        { role: 'quit' },
      ],
    }] : []),
    {
      label: 'File',
      submenu: [
        { label: 'Add account…', accelerator: 'CmdOrCtrl+T', click: promptAdd },
        { type: 'separator' },
        IS_MAC ? { role: 'close' } : { role: 'quit' },
      ],
    },
    { role: 'editMenu' },
    {
      label: 'View',
      submenu: [
        { label: 'Reload', accelerator: 'CmdOrCtrl+R', enabled: hasActive, click: () => reloadActive(false) },
        { label: 'Reload (clear cache)', accelerator: 'CmdOrCtrl+Shift+R', enabled: hasActive, click: () => reloadActive(true) },
        { type: 'separator' },
        { label: 'Zoom in', accelerator: 'CmdOrCtrl+Plus', enabled: hasActive, click: () => zoom(0.5) },
        { label: 'Zoom out', accelerator: 'CmdOrCtrl+-', enabled: hasActive, click: () => zoom(-0.5) },
        { label: 'Actual size', accelerator: 'CmdOrCtrl+0', enabled: hasActive, click: () => zoom(0) },
        { type: 'separator' },
        { role: 'togglefullscreen' },
      ],
    },
    {
      label: 'Account',
      submenu: [
        ...accounts,
        ...(accounts.length ? [{ type: 'separator' }] : []),
        { label: 'Next account', accelerator: 'Ctrl+Tab', enabled: accounts.length > 1, click: () => cycle(1) },
        { label: 'Previous account', accelerator: 'Ctrl+Shift+Tab', enabled: accounts.length > 1, click: () => cycle(-1) },
        { type: 'separator' },
        { label: 'Rename…', enabled: hasActive, click: () => promptRename(store.activeId) },
        { label: 'Paste sign-in link', enabled: hasActive, click: () => pasteSignInLink(store.activeId) },
        { label: 'Delete account…', enabled: hasActive, click: () => deleteAccount(store.activeId) },
      ],
    },
    {
      role: 'help',
      submenu: [
        { label: 'How to use', click: showHelp },
        { label: 'GitHub page', click: () => shell.openExternal(BRAND.repo) },
        ...(IS_MAC ? [] : [{ type: 'separator' }, { label: 'About', click: showAbout }]),
      ],
    },
  ];
  Menu.setApplicationMenu(Menu.buildFromTemplate(template));
}

function zoom(step) {
  const view = views.get(store.activeId);
  if (!view) return;
  const wc = view.webContents;
  wc.setZoomLevel(step === 0 ? 0 : Math.max(-3, Math.min(3, wc.getZoomLevel() + step)));
}

// ---------------------------------------------------------------- window

function validBounds(b) {
  if (!b || ![b.x, b.y, b.width, b.height].every(Number.isFinite)) return null;
  if (b.width < 720 || b.height < 480) return null;
  const visible = screen.getAllDisplays().some(d => {
    const a = d.workArea;
    return b.x < a.x + a.width - 80 && b.x + b.width > a.x + 80 && b.y >= a.y - 10 && b.y < a.y + a.height - 80;
  });
  return visible ? b : null;
}

function createWindow() {
  const bounds = validBounds(store.bounds) || { width: 1280, height: 860 };
  win = new BrowserWindow({
    ...bounds,
    minWidth: 720,
    minHeight: 480,
    title: BRAND.name,
    backgroundColor: '#F6F5F2',
    show: false,
    autoHideMenuBar: true,
    titleBarStyle: IS_MAC ? 'hiddenInset' : 'default',
    trafficLightPosition: IS_MAC ? { x: 14, y: 16 } : undefined,
    icon: IS_MAC ? undefined : path.join(__dirname, 'mark.png'),
    webPreferences: {
      preload: path.join(__dirname, 'preload.js'),
      contextIsolation: true,
      sandbox: true,
      nodeIntegration: false,
    },
  });
  if (store.maximized) win.maximize();
  win.webContents.on('will-navigate', e => e.preventDefault());
  win.webContents.setWindowOpenHandler(() => ({ action: 'deny' }));
  win.on('resize', layout);
  win.on('enter-full-screen', () => win.webContents.send('fullscreen', true));
  win.on('leave-full-screen', () => win.webContents.send('fullscreen', false));
  win.on('close', () => {
    store.bounds = win.getNormalBounds();
    store.maximized = win.isMaximized();
    saveStore();
  });
  win.on('closed', () => { win = null; views.clear(); });
  win.once('ready-to-show', () => { if (!SELFTEST || process.env.CMAL_SHOW) win.show(); });
  win.webContents.once('did-finish-load', () => {
    pushState();
    if (store.activeId) selectAccount(store.activeId);
    if (SELFTEST === 'ui') setTimeout(promptAdd, 1500); // screenshot of the add dialog
    else if (SELFTEST) runSelfTest().catch(e => { console.error('[selftest] FAILED', e); app.exit(1); });
  });
  win.loadFile(path.join(__dirname, 'index.html'));
}

// Messages from the tab bar. Only the app's own tab bar page may send them.
function fromUi(handler) {
  return (event, ...args) => {
    if (!win || event.sender !== win.webContents) return undefined;
    return handler(...args);
  };
}
ipcMain.handle('get-state', fromUi(() => ({
  accounts: store.accounts, activeId: store.activeId, platform: process.platform,
})));
ipcMain.on('add', fromUi(name => addAccount(name)));
ipcMain.on('rename', fromUi((id, name) => renameAccount(id, name)));
ipcMain.on('select', fromUi(id => selectAccount(id)));
ipcMain.on('prompt-add', fromUi(() => promptAdd()));
ipcMain.on('tab-menu', fromUi(id => popup(accountMenuItems(id))));
ipcMain.on('more-menu', fromUi(() => popup([
  ...accountMenuItems(store.activeId),
  ...(activeAccount() ? [{ type: 'separator' }] : []),
  { label: 'How to use', click: showHelp },
  { label: 'About', click: showAbout },
])));
ipcMain.on('accounts-menu', fromUi(() => popup([
  ...store.accounts.map(a => ({
    label: a.name + (a.signedIn ? '' : '  (not signed in)'),
    type: 'radio', checked: a.id === store.activeId, click: () => selectAccount(a.id),
  })),
  ...(store.accounts.length ? [{ type: 'separator' }] : []),
  { label: 'Add account…', click: promptAdd },
  { type: 'separator' },
  { label: 'How to use', click: showHelp },
  { label: 'About', click: showAbout },
])));
ipcMain.on('reload', fromUi(() => reloadActive(false)));
ipcMain.on('about', fromUi(() => showAbout()));
ipcMain.on('overlay', fromUi(on => {
  overlay = !!on;
  layout();
  if (!overlay && views.get(store.activeId)) views.get(store.activeId).webContents.focus();
}));

// ---------------------------------------------------------------- self-test
// Used by the automated build (CMAL_SELFTEST=1 then 2) to check the app really
// works on each platform: it adds, renames, moves and deletes accounts, then a
// second run checks that everything was remembered.

async function runSelfTest() {
  const wait = ms => new Promise(r => setTimeout(r, ms));
  const results = [];
  const check = (name, ok, extra) => {
    results.push({ name, ok: !!ok, extra });
    console.log(`[selftest] ${ok ? 'PASS' : 'FAIL'} ${name}${extra ? ' :: ' + extra : ''}`);
  };
  const shots = process.env.CMAL_SHOT_DIR;
  const waitLoad = wc => new Promise(resolve => {
    const t = setTimeout(resolve, 25000);
    if (!wc.isLoading()) { clearTimeout(t); return resolve(); }
    wc.once('did-stop-loading', () => { clearTimeout(t); resolve(); });
  });

  if (SELFTEST === '1') {
    const work = addAccount('Work');
    const personal = addAccount('Personal');
    const client = addAccount('Client A');
    check('three accounts added', store.accounts.length === 3);
    check('each account has its own browser storage',
      new Set([work, personal, client].map(a => views.get(a.id).webContents.session)).size === 3);
    selectAccount(work.id);
    check('only the active tab is visible',
      [...views].every(([id, v]) => v.getVisible() === (id === work.id)));
    const ua = views.get(work.id).webContents.getUserAgent();
    check('user agent looks like normal Chrome', !/Electron|Launcher/i.test(ua) && /Chrome\//.test(ua), ua);
    renameAccount(client.id, 'Client B');
    check('rename', findAccount(client.id).name === 'Client B');
    moveAccount(client.id, -2);
    check('move', store.accounts[0].id === client.id);
    await waitLoad(views.get(personal.id).webContents);
    await wait(1500);
    const pDir = partitionDir(personal.id);
    const existedBefore = fs.existsSync(pDir);
    await deleteAccount(personal.id, true);
    check('delete removes the tab', !findAccount(personal.id) && !views.has(personal.id));
    check('delete removes its browser data', existedBefore && !fs.existsSync(pDir),
      `existed=${existedBefore} after=${fs.existsSync(pDir)}`);
    selectAccount(work.id);
    await waitLoad(views.get(work.id).webContents);
    await wait(2500);
    const url = views.get(work.id).webContents.getURL();
    check('Claude page loads in a tab', /^https:\/\//.test(url), url);
    if (shots) {
      try {
        fs.mkdirSync(shots, { recursive: true });
        fs.writeFileSync(path.join(shots, 'tabbar.png'), (await win.webContents.capturePage()).toPNG());
        fs.writeFileSync(path.join(shots, 'page.png'),
          (await views.get(work.id).webContents.capturePage()).toPNG());
      } catch (e) { console.log('[selftest] screenshot skipped:', e.message); }
    }
  } else {
    const names = store.accounts.map(a => a.name).join(',');
    check('accounts remembered after restart', names === 'Client B,Work', names);
    check('active account remembered', activeAccount() && activeAccount().name === 'Work');
    const folders = fs.readdirSync(path.join(dataDir(), 'Partitions')).filter(n => n.startsWith('acct-'));
    check('no deleted data left over',
      store.pendingDelete.length === 0 && folders.every(n => store.accounts.some(a => 'acct-' + a.id === n)),
      folders.join(','));
    const remaining = store.accounts.every(a => fs.existsSync(partitionDir(a.id)) || !views.has(a.id));
    check('kept accounts still have their data', remaining);
  }
  const failed = results.filter(r => !r.ok).length;
  console.log(`[selftest] phase ${SELFTEST}: ${results.length - failed} passed, ${failed} failed`);
  if (process.env.CMAL_RESULTS) {
    fs.writeFileSync(process.env.CMAL_RESULTS, JSON.stringify({ phase: SELFTEST, platform: process.platform, results }, null, 2));
  }
  setTimeout(() => app.exit(failed ? 1 : 0), 300);
}

// ---------------------------------------------------------------- start

if (SELFTEST) {
  console.log(`[selftest] starting ${app.getVersion()} on ${process.platform}-${process.arch}, data ${app.getPath('userData')}`);
  process.on('exit', code => console.log('[selftest] exit ' + code));
}
if (!app.requestSingleInstanceLock()) {
  if (SELFTEST) console.log('[selftest] FAIL another copy is already running');
  app.quit();
} else {
  app.on('second-instance', () => {
    if (!win) return;
    if (win.isMinimized()) win.restore();
    win.show();
    win.focus();
  });
  app.whenReady().then(() => {
    loadStore();
    purgeDeleted(true);
    buildAppMenu();
    createWindow();
  });
  app.on('activate', () => { if (!win) createWindow(); });
  app.on('window-all-closed', () => app.quit());
  // Pages inside tabs may not open new app windows except sign-in pop-ups,
  // and may not run as a webview.
  app.on('web-contents-created', (e, contents) => {
    contents.on('will-attach-webview', ev => ev.preventDefault());
    if (contents.getType() === 'window') {
      // Sign-in pop-ups: further pop-ups go to the normal browser.
      setImmediate(() => {
        if (win && contents === win.webContents) return;
        contents.setWindowOpenHandler(({ url }) => {
          if (hostIn(httpsHost(url), SIGNIN_HOSTS)) return { action: 'allow' };
          if (/^https?:\/\//i.test(url)) shell.openExternal(url);
          return { action: 'deny' };
        });
      });
    }
  });
}
