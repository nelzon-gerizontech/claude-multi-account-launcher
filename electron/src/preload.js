'use strict';
// Bridge between the tab bar page and the app. Only these calls are exposed.
const { contextBridge, ipcRenderer } = require('electron');

contextBridge.exposeInMainWorld('cmal', {
  getState: () => ipcRenderer.invoke('get-state'),
  add: name => ipcRenderer.send('add', String(name)),
  rename: (id, name) => ipcRenderer.send('rename', String(id), String(name)),
  select: id => ipcRenderer.send('select', String(id)),
  promptAdd: () => ipcRenderer.send('prompt-add'),
  tabMenu: id => ipcRenderer.send('tab-menu', String(id)),
  moreMenu: () => ipcRenderer.send('more-menu'),
  accountsMenu: () => ipcRenderer.send('accounts-menu'),
  reload: () => ipcRenderer.send('reload'),
  back: () => ipcRenderer.send('back'),
  home: () => ipcRenderer.send('home'),
  about: () => ipcRenderer.send('about'),
  setOverlay: on => ipcRenderer.send('overlay', !!on),
  onState: fn => ipcRenderer.on('state', (e, s) => fn(s)),
  onPrompt: fn => ipcRenderer.on('prompt', (e, p) => fn(p)),
  onFullscreen: fn => ipcRenderer.on('fullscreen', (e, on) => fn(on)),
});
