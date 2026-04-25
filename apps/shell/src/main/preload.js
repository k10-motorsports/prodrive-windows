const { contextBridge, ipcRenderer } = require('electron');

contextBridge.exposeInMainWorld('racecor', {
  version: () => {
    return new Promise((resolve) => {
      ipcRenderer.once('get-version', (event, version) => {
        resolve(version);
      });
      ipcRenderer.send('get-version');
    });
  },

  platform: () => process.platform,

  openExternal: (url) => {
    ipcRenderer.send('open-external', url);
  },

  onDeepLink: (callback) => {
    ipcRenderer.on('deep-link', (event, url) => {
      callback(url);
    });
  }
});
