const { app, BrowserWindow, Menu, ipcMain, shell } = require('electron');
const { autoUpdater } = require('electron-updater');
const path = require('path');
const WindowState = require('./window-state');

const isDev = process.env.NODE_ENV === 'development' || require('electron-squirrel-startup');
const RACECOR_URL = process.env.RACECOR_URL || 'https://prodrive.racecor.io';

let mainWindow;
let windowState;

// Single-instance lock for Windows — critical for custom URL scheme callbacks
const gotTheLock = app.requestSingleInstanceLock();
if (!gotTheLock) {
  app.quit();
} else {
  app.on('second-instance', (event, argv) => {
    // Handle deep-link from the second instance
    handleDeepLink(argv);
    if (mainWindow) {
      if (mainWindow.isMinimized()) mainWindow.restore();
      mainWindow.focus();
    }
  });
}

function createWindow() {
  // Initialize window state
  windowState = new WindowState({
    defaultWidth: 1400,
    defaultHeight: 900
  });

  mainWindow = new BrowserWindow({
    x: windowState.x,
    y: windowState.y,
    width: windowState.width,
    height: windowState.height,
    webPreferences: {
      preload: path.join(__dirname, 'preload.js'),
      contextIsolation: true,
      nodeIntegration: false,
      enableRemoteModule: false,
      sandbox: true
    },
    icon: path.join(__dirname, '..', '..', 'assets', 'icon.ico')
  });

  // Load the web app
  mainWindow.loadURL(RACECOR_URL);

  // Handle window-close
  mainWindow.on('close', () => {
    windowState.saveState(mainWindow);
  });

  mainWindow.on('closed', () => {
    mainWindow = null;
  });

  // Open DevTools in dev mode
  if (isDev) {
    mainWindow.webContents.openDevTools();
  }

  // Hosts that must stay in this window instead of being opened in the
  // default browser. Discord OAuth MUST stay in-window: NextAuth sets
  // PKCE/state cookies in this window's session when the flow starts; if
  // the authorize URL opens in the system browser, the callback hits a
  // different cookie jar and NextAuth fails with `InvalidCheck` (shown in
  // the app window as "Server error / There is a problem with the server
  // configuration"). Cloudflare challenges may appear during Discord auth.
  const INTERNAL_NAV_HOSTS = new Set([
    'discord.com',
    'discordapp.com',
    'cdn.discordapp.com',
    'challenges.cloudflare.com',
  ]);
  function isInternalHost(hostname) {
    const main = new URL(RACECOR_URL).hostname;
    if (hostname === main) return true;
    if (INTERNAL_NAV_HOSTS.has(hostname)) return true;
    for (const allowed of INTERNAL_NAV_HOSTS) {
      if (hostname.endsWith('.' + allowed)) return true;
    }
    return false;
  }

  // Handle navigation — keep same-origin + allowlisted auth hosts, open
  // everything else in default browser.
  mainWindow.webContents.on('will-navigate', (event, url) => {
    const targetUrl = new URL(url);
    if (!isInternalHost(targetUrl.hostname)) {
      event.preventDefault();
      shell.openExternal(url);
    }
  });

  // Handle window.open() — same policy.
  mainWindow.webContents.setWindowOpenHandler(({ url }) => {
    const targetUrl = new URL(url);
    if (!isInternalHost(targetUrl.hostname)) {
      shell.openExternal(url);
      return { action: 'deny' };
    }
    return { action: 'allow' };
  });
}

// Register custom URL scheme handler.
//
// Shell uses `racecor-prodrive://`; the native WinUI 3 app uses its own
// distinct `racecor-native://` scheme so the two bundles don't fight over
// a single Launch Services / shell-protocol registration.
function registerProtocolHandler() {
  if (isDev) {
    // In dev, register the protocol
    if (process.defaultApp) {
      if (process.argv.length >= 2) {
        app.setAsDefaultProtocolClient('racecor-prodrive', process.execPath, [path.resolve(process.argv[1])]);
      }
    } else {
      app.setAsDefaultProtocolClient('racecor-prodrive');
    }
  } else {
    app.setAsDefaultProtocolClient('racecor-prodrive');
  }
}

function handleDeepLink(argv) {
  // Find URL in argv (format: racecor-prodrive://...)
  for (let i = 0; i < argv.length; i++) {
    if (argv[i].startsWith('racecor-prodrive://')) {
      const url = argv[i];
      // Send to renderer process
      if (mainWindow && mainWindow.webContents) {
        mainWindow.webContents.send('deep-link', url);
      }
      break;
    }
  }
}

app.on('ready', () => {
  registerProtocolHandler();
  createWindow();
  setupMenu();

  // Set up auto-updater
  if (!isDev) {
    autoUpdater.checkForUpdatesAndNotify();
  }
});

app.on('window-all-closed', () => {
  // On Windows, typically quit when all windows are closed
  app.quit();
});

app.on('activate', () => {
  // On macOS, re-create window when app is activated
  if (mainWindow === null) {
    createWindow();
  }
});

// Handle custom URL scheme protocol
app.on('open-url', (event, url) => {
  event.preventDefault();
  if (mainWindow) {
    mainWindow.webContents.send('deep-link', url);
    if (mainWindow.isMinimized()) mainWindow.restore();
    mainWindow.focus();
  } else {
    // Queue the URL for when window opens
    global.pendingDeepLink = url;
  }
});

// IPC handlers
ipcMain.on('get-version', (event) => {
  event.reply('get-version', app.getVersion());
});

ipcMain.on('open-external', (event, url) => {
  shell.openExternal(url);
});

function setupMenu() {
  // Hide default menu on Windows — user can still use keyboard shortcuts
  Menu.setApplicationMenu(null);

  // On Windows, provide minimal context menu
  if (process.platform === 'win32') {
    mainWindow.webContents.on('context-menu', (event) => {
      const template = [
        { label: 'Back', click: () => mainWindow.webContents.goBack() },
        { label: 'Forward', click: () => mainWindow.webContents.goForward() },
        { label: 'Reload', click: () => mainWindow.webContents.reload() },
        { type: 'separator' },
        { label: 'Inspect', click: () => mainWindow.webContents.openDevTools() }
      ];
      const menu = Menu.buildFromTemplate(template);
      menu.popup({ window: mainWindow });
    });
  }
}

// Squirrel installer events (Windows NSIS)
if (require('electron-squirrel-startup')) {
  app.quit();
}
