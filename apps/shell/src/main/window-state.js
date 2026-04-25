const path = require('path');
const fs = require('fs');
const os = require('os');

class WindowState {
  constructor(options = {}) {
    // Determine config path: %APPDATA%/RaceCor Pro Drive/ on Windows
    const appDataPath = process.env.APPDATA || path.join(os.homedir(), 'AppData', 'Roaming');
    this.configDir = path.join(appDataPath, 'RaceCor Pro Drive');
    this.configPath = path.join(this.configDir, 'window-state.json');

    this.defaultWidth = options.defaultWidth || 1400;
    this.defaultHeight = options.defaultHeight || 900;

    this.x = undefined;
    this.y = undefined;
    this.width = this.defaultWidth;
    this.height = this.defaultHeight;
    this.isMaximized = false;

    this.load();
  }

  load() {
    try {
      if (fs.existsSync(this.configPath)) {
        const data = JSON.parse(fs.readFileSync(this.configPath, 'utf-8'));
        this.x = data.x;
        this.y = data.y;
        this.width = data.width;
        this.height = data.height;
        this.isMaximized = data.isMaximized;
      }
    } catch (e) {
      console.warn('Failed to load window state:', e);
    }
  }

  saveState(window) {
    try {
      // Ensure config directory exists
      if (!fs.existsSync(this.configDir)) {
        fs.mkdirSync(this.configDir, { recursive: true });
      }

      const bounds = window.getBounds();
      const state = {
        x: bounds.x,
        y: bounds.y,
        width: bounds.width,
        height: bounds.height,
        isMaximized: window.isMaximized()
      };

      fs.writeFileSync(this.configPath, JSON.stringify(state, null, 2));
    } catch (e) {
      console.warn('Failed to save window state:', e);
    }
  }
}

module.exports = WindowState;
