'use strict';
// Tab bar for Claude Multi-Account Launcher.
(function () {
  const COLORS = ['#FF6600', '#1A1A2E', '#2E7D6B', '#7A4FD1', '#C0392B', '#2A6FDB', '#B8860B', '#D35490'];
  const api = window.cmal;
  const $ = id => document.getElementById(id);
  const tabs = $('tabs');
  let state = { accounts: [], activeId: null, platform: '' };
  let prompt = null; // { mode: 'add' | 'rename', id }

  function initials(name) {
    const words = name.trim().split(/\s+/).filter(Boolean);
    if (!words.length) return '?';
    const letters = words.length > 1 ? words[0][0] + words[1][0] : words[0][0];
    return letters.toUpperCase();
  }

  function render() {
    document.body.classList.toggle('mac', state.platform === 'darwin');
    tabs.textContent = '';
    state.accounts.forEach((a, i) => {
      const tab = document.createElement('button');
      tab.className = 'tab' + (a.id === state.activeId ? ' active' : '');
      tab.title = a.name + (a.signedIn ? '' : ' (not signed in)') + (i < 9 ? `  ·  shortcut ${state.platform === 'darwin' ? '⌘' : 'Ctrl+'}${i + 1}` : '');
      tab.setAttribute('role', 'tab');
      tab.setAttribute('aria-selected', String(a.id === state.activeId));

      const avatar = document.createElement('span');
      avatar.className = 'avatar';
      avatar.style.background = COLORS[a.color % COLORS.length];
      avatar.textContent = initials(a.name);
      const name = document.createElement('span');
      name.className = 'name';
      name.textContent = a.name;
      tab.append(avatar, name);
      if (!a.signedIn) {
        const dot = document.createElement('span');
        dot.className = 'dot';
        dot.title = 'Not signed in';
        tab.append(dot);
      }
      tab.addEventListener('click', () => api.select(a.id));
      tab.addEventListener('dblclick', () => openPrompt({ mode: 'rename', id: a.id, name: a.name }));
      tab.addEventListener('contextmenu', e => { e.preventDefault(); api.tabMenu(a.id); });
      tab.addEventListener('auxclick', e => { if (e.button === 1) api.tabMenu(a.id); });
      tabs.append(tab);
    });
    const active = tabs.querySelector('.tab.active');
    if (active) active.scrollIntoView({ block: 'nearest', inline: 'nearest' });
    $('empty').hidden = state.accounts.length > 0;
    $('reload').disabled = !state.activeId;
    $('more').disabled = false;
  }

  function openPrompt(p) {
    prompt = p;
    const add = p.mode === 'add';
    $('modal-title').textContent = add ? 'Add account' : 'Rename account';
    $('modal-ok').textContent = add ? 'Add' : 'Save';
    $('modal-hint').hidden = !add;
    $('modal-name').value = add ? '' : (p.name || '');
    $('modal').hidden = false;
    api.setOverlay(true);
    setTimeout(() => { $('modal-name').focus(); $('modal-name').select(); }, 0);
  }

  function closePrompt() {
    prompt = null;
    $('modal').hidden = true;
    api.setOverlay(false);
  }

  $('modal-form').addEventListener('submit', e => {
    e.preventDefault();
    const name = $('modal-name').value.trim();
    if (!name || !prompt) return;
    if (prompt.mode === 'add') api.add(name); else api.rename(prompt.id, name);
    closePrompt();
  });
  $('modal-cancel').addEventListener('click', closePrompt);
  $('modal').addEventListener('mousedown', e => { if (e.target === $('modal')) closePrompt(); });
  document.addEventListener('keydown', e => { if (e.key === 'Escape' && prompt) closePrompt(); });

  $('brand').addEventListener('click', () => api.about());
  $('add').addEventListener('click', () => openPrompt({ mode: 'add' }));
  $('empty-add').addEventListener('click', () => openPrompt({ mode: 'add' }));
  $('accounts').addEventListener('click', () => api.accountsMenu());
  $('reload').addEventListener('click', () => api.reload());
  $('more').addEventListener('click', () => api.moreMenu());
  tabs.addEventListener('wheel', e => {
    if (Math.abs(e.deltaY) > Math.abs(e.deltaX)) { tabs.scrollLeft += e.deltaY; e.preventDefault(); }
  }, { passive: false });

  api.onState(s => { state = s; render(); });
  api.onPrompt(p => openPrompt(p));
  api.onFullscreen(on => document.body.classList.toggle('fullscreen', on));
  api.getState().then(s => { state = s; render(); });
})();
