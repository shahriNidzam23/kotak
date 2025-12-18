// ============================
// KOTAK TV Launcher UI
// ============================

// C# Bridge (exposed via WebView2)
const bridge = window.chrome?.webview?.hostObjects?.sync?.bridge;
const bridgeAsync = window.chrome?.webview?.hostObjects?.bridge;

// State management
const state = {
    apps: [],
    wifiNetworks: [],
    currentScreen: 'main-screen',
    focusedIndex: 0,
    focusableElements: [],
    pendingWifiSsid: null,
    pendingWifiSecured: false,
    pendingConfirmAction: null,
    pendingAppPath: null,
    pendingAppThumbnail: null,
    selectedAppName: null,
    // Controller mapping
    controllerConfig: null,
    mappingButton: null, // Button being mapped (a, b, x, y, lb, rb, back, start)
    isMappingMode: false,
    // Cursor auto-hide
    cursorHideTimeout: null,
    isCursorVisible: true,
    // Update download state
    updateDownloading: false,
    updatePhase: null,
    updateProgress: 0
};

// ============================
// Initialization
// ============================
document.addEventListener('DOMContentLoaded', async () => {
    // Show splash screen and initialize app
    await initializeApp();
});

async function initializeApp() {
    const splashScreen = document.getElementById('splash-screen');
    const urlParams = new URLSearchParams(window.location.search);
    const skipSplash = urlParams.get('skipSplash') === '1';

    // Load data while splash is showing
    await loadApps();
    await updateWifiStatus();

    // Setup timers and listeners
    updateClock();
    setInterval(updateClock, 1000);
    setInterval(updateWifiStatus, 30000); // Update Wi-Fi status every 30s
    setupGamepadListener();
    setupMouseClickHandlers();
    setupCursorAutoHide();
    updateFocusableElements();
    loadVersion();

    if (skipSplash) {
        // Skip splash animation (returning from app via LB+RB+Start combo)
        splashScreen.classList.add('hidden');
        focusElement(0);
    } else {
        // Wait for splash animation to complete (minimum 3 seconds)
        await new Promise(resolve => setTimeout(resolve, 3000));

        // Hide splash screen with fade out
        splashScreen.classList.add('hidden');

        // Focus first element after splash is hidden
        setTimeout(() => {
            focusElement(0);
        }, 500);
    }
}

// ============================
// App Loading & Rendering
// ============================
async function loadApps() {
    try {
        if (bridge) {
            const appsJson = bridge.GetApps();
            state.apps = JSON.parse(appsJson);
        } else {
            // Mock data for development
            state.apps = [
                { name: 'Netflix', type: 'web', url: 'https://netflix.com', thumbnail: 'thumbnails/netflix.png' },
                { name: 'YouTube', type: 'web', url: 'https://youtube.com', thumbnail: 'thumbnails/youtube.png' },
                { name: 'Disney+', type: 'web', url: 'https://disneyplus.com', thumbnail: 'thumbnails/disney.png' }
            ];
        }
        renderAppGrid();
    } catch (error) {
        console.error('Failed to load apps:', error);
        state.apps = [];
        renderAppGrid();
    }
}

function refreshConfig() {
    if (!bridge) return;

    try {
        const success = bridge.ReloadConfig();
        if (success) {
            // Reload apps and re-render
            loadApps();
            // Show brief success feedback
            showToast('Config refreshed successfully');
        } else {
            showToast('Failed to refresh config');
        }
    } catch (e) {
        console.error('Error refreshing config:', e);
        showToast('Error refreshing config');
    }
}

function showToast(message) {
    // Create toast element if it doesn't exist
    let toast = document.getElementById('toast-message');
    if (!toast) {
        toast = document.createElement('div');
        toast.id = 'toast-message';
        toast.className = 'toast-message';
        document.body.appendChild(toast);
    }

    toast.textContent = message;
    toast.classList.add('show');

    // Hide after 2 seconds
    setTimeout(() => {
        toast.classList.remove('show');
    }, 2000);
}

function renderAppGrid() {
    const grid = document.getElementById('app-grid');
    grid.innerHTML = '';

    // Render app tiles
    state.apps.forEach((app, index) => {
        const tile = createAppTile(app, index);
        grid.appendChild(tile);
    });

    // Add "Add App" tile
    const addTile = document.createElement('div');
    addTile.className = 'app-tile add-app focusable';
    addTile.dataset.action = 'add-app';
    addTile.tabIndex = 0;
    addTile.innerHTML = `
        <span class="icon">+</span>
        <span class="label">Add App</span>
    `;
    grid.appendChild(addTile);

    updateFocusableElements();
}

function createAppTile(app, index) {
    const tile = document.createElement('div');
    tile.className = 'app-tile focusable';
    tile.dataset.appName = app.name;
    tile.dataset.action = 'launch-app';
    tile.tabIndex = 0;

    // Generate thumbnail HTML
    let thumbnailContent = '';
    if (app.thumbnail) {
        // Convert thumbnail path to use virtual host
        let thumbnailUrl = app.thumbnail;
        if (thumbnailUrl.startsWith('thumbnails/')) {
            thumbnailUrl = 'https://kotakthumbs.local/' + thumbnailUrl.substring(11);
        }
        thumbnailContent = `<div class="thumbnail" style="background-image: url('${escapeHtml(thumbnailUrl)}')"></div>`;
    } else {
        // Use a placeholder icon based on app type
        const icon = app.type === 'web' ? '&#127760;' : '&#128187;';
        thumbnailContent = `<div class="thumbnail"><span class="placeholder-icon">${icon}</span></div>`;
    }

    tile.innerHTML = `
        ${thumbnailContent}
        <div class="app-name">${escapeHtml(app.name)}</div>
    `;

    return tile;
}

// ============================
// Screen Navigation
// ============================
function showScreen(screenId) {
    // Hide context menu when changing screens
    hideContextMenu();

    document.querySelectorAll('.screen').forEach(screen => {
        screen.classList.remove('active');
    });
    document.getElementById(screenId).classList.add('active');
    state.currentScreen = screenId;
    state.focusedIndex = 0;
    updateFocusableElements();
    focusElement(0);
}

function goBack() {
    // Hide any open dialogs first
    hideAllDialogs();
    hideContextMenu();

    // Handle IPTV view navigation
    if (state.currentTab === 'iptv' && state.currentScreen === 'main-screen') {
        if (state.iptvView === 'player') {
            iptvBackToChannels();
            return;
        } else if (state.iptvView === 'channels') {
            iptvBackToPlaylists();
            return;
        }
    }

    if (state.currentScreen !== 'main-screen') {
        showScreen('main-screen');
    }
}

function hideAllDialogs() {
    document.querySelectorAll('.dialog').forEach(dialog => {
        dialog.classList.add('hidden');
    });
    state.pendingWifiSsid = null;
    state.pendingConfirmAction = null;
    state.pendingAppPath = null;
    state.pendingAppThumbnail = null;

    // Stop controller mapping if active
    if (state.isMappingMode) {
        cancelControllerMapping();
    }
}

// ============================
// Wi-Fi Management
// ============================
async function updateWifiStatus() {
    try {
        if (bridge) {
            const statusJson = bridge.GetWifiStatus();
            const status = JSON.parse(statusJson);

            // Update settings tab Wi-Fi display
            const settingsWifiLabel = document.getElementById('settings-wifi-label');
            const settingsWifiIcon = document.getElementById('settings-wifi-icon');

            if (status.connected && status.ssid) {
                if (settingsWifiLabel) settingsWifiLabel.textContent = status.ssid;
                if (settingsWifiIcon) settingsWifiIcon.innerHTML = '&#128246;'; // Connected icon
            } else {
                if (settingsWifiLabel) settingsWifiLabel.textContent = 'Not connected';
                if (settingsWifiIcon) settingsWifiIcon.innerHTML = '&#128246;'; // Disconnected icon
            }
        }
    } catch (error) {
        console.error('Failed to get Wi-Fi status:', error);
    }
}

async function showWifiScreen() {
    showScreen('wifi-screen');
    await loadWifiNetworks();
}

async function loadWifiNetworks() {
    const list = document.getElementById('wifi-list');
    const currentDisplay = document.getElementById('wifi-current');
    list.innerHTML = '<div class="loading"><div class="spinner"></div></div>';

    try {
        if (bridge) {
            // Get current connection
            const statusJson = bridge.GetWifiStatus();
            const status = JSON.parse(statusJson);
            currentDisplay.textContent = status.connected ? `Connected to: ${status.ssid}` : 'Not connected';

            // Get networks
            const networksJson = bridge.GetWifiNetworks();
            state.wifiNetworks = JSON.parse(networksJson);
        } else {
            // Mock data
            currentDisplay.textContent = 'Connected to: Home WiFi';
            state.wifiNetworks = [
                { ssid: 'Home WiFi', signalStrength: 90, isSecured: true, isConnected: true },
                { ssid: 'Guest Network', signalStrength: 75, isSecured: true, isConnected: false },
                { ssid: 'Neighbor WiFi', signalStrength: 45, isSecured: true, isConnected: false }
            ];
        }
        renderWifiList();
    } catch (error) {
        console.error('Failed to load Wi-Fi networks:', error);
        list.innerHTML = '<div class="empty-state"><span class="icon">&#128543;</span><p>Failed to scan networks</p></div>';
    }
}

function renderWifiList() {
    const list = document.getElementById('wifi-list');
    list.innerHTML = '';

    if (state.wifiNetworks.length === 0) {
        list.innerHTML = '<div class="empty-state"><span class="icon">&#128246;</span><p>No networks found</p></div>';
        updateFocusableElements();
        return;
    }

    state.wifiNetworks.forEach(network => {
        const item = document.createElement('div');
        item.className = `wifi-item focusable ${network.isConnected ? 'connected' : ''}`;
        item.dataset.action = 'select-wifi';
        item.dataset.ssid = network.ssid;
        item.dataset.secured = network.isSecured;
        item.dataset.connected = network.isConnected;
        item.tabIndex = 0;

        const signalIcon = getSignalIcon(network.signalStrength);
        const lockIcon = network.isSecured ? '&#128274;' : '';

        item.innerHTML = `
            <span class="wifi-icon">${signalIcon}</span>
            <div class="wifi-info">
                <div class="wifi-ssid">${escapeHtml(network.ssid)} ${lockIcon}</div>
                <div class="wifi-status">${network.isConnected ? 'Connected' : (network.isSecured ? 'Secured' : 'Open')}</div>
            </div>
            <span class="wifi-signal">${network.signalStrength}%</span>
        `;

        list.appendChild(item);
    });

    updateFocusableElements();
}

function getSignalIcon(strength) {
    if (strength >= 75) return '&#128246;';
    if (strength >= 50) return '&#128246;';
    if (strength >= 25) return '&#128246;';
    return '&#128246;';
}

function selectWifi(ssid, isSecured, isConnected) {
    if (isConnected) {
        // Already connected - could show disconnect option
        return;
    }

    state.pendingWifiSsid = ssid;
    state.pendingWifiSecured = isSecured;

    if (isSecured) {
        promptWifiPassword(ssid);
    } else {
        connectToWifi('');
    }
}

function promptWifiPassword(ssid) {
    document.getElementById('wifi-ssid-label').textContent = `Network: ${ssid}`;
    document.getElementById('wifi-password').value = '';
    document.getElementById('password-dialog').classList.remove('hidden');
    updateFocusableElements();

    // Focus the password input
    setTimeout(() => {
        document.getElementById('wifi-password').focus();
    }, 100);
}

async function connectToWifi(password) {
    const ssid = state.pendingWifiSsid;
    hideAllDialogs();

    if (bridge && ssid) {
        const success = bridge.ConnectToWifi(ssid, password || '');
        if (success) {
            await loadWifiNetworks();
            await updateWifiStatus();
        } else {
            showConfirmDialog('Connection Failed', `Failed to connect to ${ssid}. Please check the password and try again.`, null);
        }
    }

    state.pendingWifiSsid = null;
}

// ============================
// Settings & System Actions
// ============================

// ============================
// Brightness Controls
// ============================

async function loadBrightness() {
    if (!bridge) return;

    try {
        // Get brightness (may not be supported on desktops)
        const brightnessSupported = bridge.IsBrightnessSupported();
        const brightnessSlider = document.querySelector('.settings-slider-item:has(#brightness-value)');

        if (brightnessSupported) {
            const brightness = bridge.GetBrightness();
            updateBrightnessDisplay(brightness);
            if (brightnessSlider) brightnessSlider.style.display = '';
        } else {
            // Hide brightness control on desktops
            if (brightnessSlider) brightnessSlider.style.display = 'none';
        }
    } catch (e) {
        console.error('Error loading brightness:', e);
    }
}

function loadVersion() {
    if (!bridge) return;

    try {
        const version = bridge.GetVersion();
        const versionText = `v${version}`;

        // Update settings version
        const versionEl = document.getElementById('app-version');
        if (versionEl) {
            versionEl.textContent = versionText;
        }

        // Update footer version
        const footerVersionEl = document.getElementById('footer-version');
        if (footerVersionEl) {
            footerVersionEl.textContent = versionText;
        }
    } catch (e) {
        console.error('Error loading version:', e);
    }
}

// ============================
// Update Functions
// ============================
let updateInfo = null;

function checkForUpdates() {
    if (!bridge) return;

    const statusEl = document.getElementById('update-status');
    const textEl = document.getElementById('update-text');
    const btnEl = document.getElementById('check-update-btn');
    const availableEl = document.getElementById('update-available');

    // Show checking state
    statusEl.classList.add('checking');
    textEl.textContent = 'Checking for updates...';
    if (btnEl) btnEl.style.display = 'none';
    if (availableEl) availableEl.style.display = 'none';

    try {
        const json = bridge.CheckForUpdates();
        const result = JSON.parse(json);
        updateInfo = result;

        statusEl.classList.remove('checking');

        if (result.error) {
            textEl.textContent = `Error: ${result.error}`;
            textEl.classList.add('update-error');
            if (btnEl) btnEl.style.display = '';
        } else if (result.hasUpdate) {
            textEl.textContent = `Update available!`;
            textEl.classList.remove('update-error');
            textEl.classList.add('update-up-to-date');

            // Show update available section
            if (availableEl) {
                availableEl.style.display = '';
                const versionEl = document.getElementById('update-latest-version');
                if (versionEl) versionEl.textContent = `v${result.latestVersion}`;
            }
            if (btnEl) btnEl.style.display = 'none';
            updateFocusableElements();
        } else {
            textEl.textContent = `You're up to date! (v${result.currentVersion})`;
            textEl.classList.remove('update-error');
            textEl.classList.add('update-up-to-date');
            if (btnEl) btnEl.style.display = '';
        }
    } catch (e) {
        console.error('Error checking for updates:', e);
        statusEl.classList.remove('checking');
        textEl.textContent = 'Failed to check for updates';
        textEl.classList.add('update-error');
        if (btnEl) btnEl.style.display = '';
    }
}

function downloadUpdate() {
    if (!bridge || !updateInfo || !updateInfo.downloadUrl) return;
    if (state.updateDownloading) return;

    state.updateDownloading = true;

    // Show progress UI
    const availableEl = document.getElementById('update-available');
    const progressEl = document.getElementById('update-progress');
    const errorEl = document.getElementById('update-error-message');

    if (availableEl) availableEl.style.display = 'none';
    if (progressEl) progressEl.style.display = '';
    if (errorEl) errorEl.style.display = 'none';

    updateProgressUI('download', 0, 'Starting download...');
    updateFocusableElements();

    try {
        const resultJson = bridge.StartUpdateDownload();
        const result = JSON.parse(resultJson);

        if (!result.success) {
            showUpdateError(result.error || 'Failed to start download');
        }
    } catch (e) {
        console.error('Error starting update download:', e);
        showUpdateError('Failed to start download');
    }
}

function updateProgressUI(phase, progress, message) {
    const phaseEl = document.getElementById('update-phase');
    const progressFill = document.getElementById('update-progress-fill');
    const progressText = document.getElementById('update-progress-text');
    const progressPercent = document.getElementById('update-progress-percent');

    if (phaseEl) {
        const phaseLabels = {
            'download': 'Downloading',
            'extract': 'Extracting',
            'install': 'Installing'
        };
        phaseEl.textContent = phaseLabels[phase] || phase;
    }

    if (progressFill) progressFill.style.width = `${progress}%`;
    if (progressText) progressText.textContent = message;
    if (progressPercent) progressPercent.textContent = `${progress}%`;

    state.updatePhase = phase;
    state.updateProgress = progress;
}

function showUpdateError(error) {
    const errorEl = document.getElementById('update-error-message');
    const progressEl = document.getElementById('update-progress');
    const availableEl = document.getElementById('update-available');

    if (progressEl) progressEl.style.display = 'none';
    if (errorEl) {
        errorEl.textContent = error;
        errorEl.style.display = '';
    }
    if (availableEl) availableEl.style.display = '';

    state.updateDownloading = false;
    updateFocusableElements();
}

function resetUpdateUI() {
    state.updateDownloading = false;
    state.updatePhase = null;
    state.updateProgress = 0;

    const availableEl = document.getElementById('update-available');
    const progressEl = document.getElementById('update-progress');
    const errorEl = document.getElementById('update-error-message');

    if (availableEl) availableEl.style.display = '';
    if (progressEl) progressEl.style.display = 'none';
    if (errorEl) errorEl.style.display = 'none';

    updateFocusableElements();
}

function cancelUpdateDownload() {
    if (!bridge || !state.updateDownloading) return;

    try {
        bridge.CancelUpdateDownload();
        resetUpdateUI();
        showToast('Update cancelled');
    } catch (e) {
        console.error('Error cancelling update:', e);
    }
}

function updateBrightnessDisplay(level) {
    level = Math.max(0, Math.min(100, level));
    document.getElementById('brightness-value').textContent = `${level}%`;
    document.getElementById('brightness-fill').style.width = `${level}%`;
}

function adjustBrightness(delta) {
    if (!bridge) return;

    try {
        const current = bridge.GetBrightness();
        const newLevel = Math.max(0, Math.min(100, current + delta));
        const success = bridge.SetBrightness(newLevel);
        if (success) {
            updateBrightnessDisplay(newLevel);
        }
    } catch (e) {
        console.error('Error adjusting brightness:', e);
    }
}

function showAddAppDialog() {
    document.getElementById('app-name-input').value = '';
    document.getElementById('app-path-input').value = '';
    state.pendingAppPath = null;
    state.pendingAppThumbnail = null;
    document.getElementById('add-app-dialog').classList.remove('hidden');
    updateFocusableElements();

    // Focus the browse button first
    setTimeout(() => {
        document.getElementById('browse-btn').focus();
    }, 100);
}

async function browseForExe() {
    if (bridge) {
        const resultJson = bridge.BrowseForExe();
        const result = JSON.parse(resultJson);

        if (result.path) {
            state.pendingAppPath = result.path;
            state.pendingAppThumbnail = result.thumbnail;
            document.getElementById('app-path-input').value = result.path;

            // Auto-fill name if empty
            const nameInput = document.getElementById('app-name-input');
            if (!nameInput.value && result.suggestedName) {
                nameInput.value = result.suggestedName;
            }
        }
    }
}

async function confirmAddApp() {
    const name = document.getElementById('app-name-input').value.trim();
    const path = state.pendingAppPath;

    if (!name) {
        document.getElementById('app-name-input').focus();
        return;
    }

    if (!path) {
        document.getElementById('browse-btn').focus();
        return;
    }

    if (bridge) {
        const thumbnail = state.pendingAppThumbnail || '';
        const success = bridge.AddApp(name, 'exe', path, thumbnail);

        if (success) {
            hideAllDialogs();
            await loadApps();
            showScreen('main-screen');
        } else {
            showConfirmDialog('Error', 'Failed to add application. An app with this name may already exist.', null);
        }
    }
}

function showRemoveAppConfirm(appName) {
    state.selectedAppName = appName;
    showConfirmDialog(
        'Remove App',
        `Are you sure you want to remove "${appName}" from the launcher?`,
        async () => {
            if (bridge && state.selectedAppName) {
                bridge.RemoveApp(state.selectedAppName);
                await loadApps();
            }
            state.selectedAppName = null;
        }
    );
}

function confirmSleep() {
    showConfirmDialog('Sleep', 'Put the PC to sleep?', () => {
        if (bridge) bridge.SleepPC();
    });
}

function confirmShutdown() {
    showConfirmDialog('Shutdown PC', 'Are you sure you want to shut down?', () => {
        if (bridge) bridge.ShutdownPC();
    });
}

function confirmRestart() {
    showConfirmDialog('Restart PC', 'Are you sure you want to restart?', () => {
        if (bridge) bridge.RestartPC();
    });
}

function exitLauncher() {
    showConfirmDialog('Exit Launcher', 'Exit KOTAK?', () => {
        if (bridge) bridge.ExitLauncher();
    });
}

// ============================
// Dialogs
// ============================
function showConfirmDialog(title, message, onConfirm) {
    document.getElementById('confirm-title').textContent = title;
    document.getElementById('confirm-message').textContent = message;
    state.pendingConfirmAction = onConfirm;
    document.getElementById('confirm-dialog').classList.remove('hidden');
    updateFocusableElements();
}

function handleConfirmYes() {
    const action = state.pendingConfirmAction;
    hideAllDialogs();
    if (action) {
        action();
    }
}

// ============================
// Context Menu
// ============================
function showContextMenu(appName, x, y) {
    state.selectedAppName = appName;
    const menu = document.getElementById('context-menu');
    menu.style.left = `${x}px`;
    menu.style.top = `${y}px`;
    menu.classList.remove('hidden');
    updateFocusableElements();
}

function hideContextMenu() {
    document.getElementById('context-menu').classList.add('hidden');
    state.selectedAppName = null;
}

// ============================
// Focus Management (TV Navigation)
// ============================
function updateFocusableElements() {
    const activeScreen = document.querySelector('.screen.active');
    const visibleDialog = document.querySelector('.dialog:not(.hidden)');
    const contextMenu = document.querySelector('.context-menu:not(.hidden)');

    const container = contextMenu || visibleDialog || activeScreen;
    if (!container) return;

    // For main screen, only get focusable elements from active tab content
    // D-pad navigation should stay within tab content only
    // Tab buttons are navigated via LB/RB only
    if (container.id === 'main-screen') {
        const activeTab = container.querySelector('.tab-content.active');
        const contentElements = activeTab ? Array.from(activeTab.querySelectorAll('.focusable:not([disabled])')) : [];
        state.focusableElements = contentElements;
    } else {
        state.focusableElements = Array.from(container.querySelectorAll('.focusable:not([disabled])'));
    }
}

function focusElement(index) {
    // Remove focus from all
    state.focusableElements.forEach(el => el.classList.remove('focused'));

    // Clamp index
    if (state.focusableElements.length === 0) return;
    state.focusedIndex = Math.max(0, Math.min(index, state.focusableElements.length - 1));

    // Add focus to target
    const element = state.focusableElements[state.focusedIndex];
    element.classList.add('focused');
    element.focus();
    element.scrollIntoView({ behavior: 'smooth', block: 'nearest' });
}

function navigateFocus(direction) {
    const elements = state.focusableElements;
    if (elements.length === 0) return;

    const current = state.focusedIndex;
    let next = current;

    // For grid navigation, calculate columns
    const grid = document.getElementById('app-grid');
    const isMainScreen = state.currentScreen === 'main-screen';
    const visibleDialog = document.querySelector('.dialog:not(.hidden)');

    if (isMainScreen && !visibleDialog && grid) {
        const gridChildren = Array.from(grid.children);
        const currentElement = elements[current];
        const isInGrid = gridChildren.includes(currentElement);

        if (isInGrid) {
            // Grid navigation
            const cols = getGridColumns();
            const gridIndex = gridChildren.indexOf(currentElement);

            switch (direction) {
                case 'up':
                    if (gridIndex >= cols) {
                        // Move up within grid
                        const targetGridIndex = gridIndex - cols;
                        next = elements.indexOf(gridChildren[targetGridIndex]);
                    }
                    break;
                case 'down':
                    if (gridIndex + cols < gridChildren.length) {
                        // Move down within grid
                        const targetGridIndex = gridIndex + cols;
                        next = elements.indexOf(gridChildren[targetGridIndex]);
                    }
                    break;
                case 'left':
                    if (gridIndex % cols > 0) {
                        next = current - 1;
                    }
                    break;
                case 'right':
                    if ((gridIndex + 1) % cols > 0 && gridIndex + 1 < gridChildren.length) {
                        next = current + 1;
                    }
                    break;
            }

            focusElement(next);
            return;
        }
    }

    // Linear navigation for non-grid elements
    switch (direction) {
        case 'up':
        case 'left':
            next = (current - 1 + elements.length) % elements.length;
            break;
        case 'down':
        case 'right':
            next = (current + 1) % elements.length;
            break;
    }

    focusElement(next);
}

function getGridColumns() {
    const grid = document.getElementById('app-grid');
    if (!grid || grid.children.length === 0) return 1;

    const gridStyle = window.getComputedStyle(grid);
    const columns = gridStyle.getPropertyValue('grid-template-columns').split(' ').length;
    return columns || 1;
}

function activateFocused() {
    const element = state.focusableElements[state.focusedIndex];
    if (!element) return;

    const action = element.dataset.action;

    switch (action) {
        case 'launch-app':
            launchApp(element.dataset.appName);
            break;
        case 'add-app':
            showAddAppDialog();
            break;
        case 'wifi':
            showWifiScreen();
            break;
        case 'back':
            goBack();
            break;
        case 'refresh-wifi':
            loadWifiNetworks();
            break;
        case 'select-wifi':
            selectWifi(
                element.dataset.ssid,
                element.dataset.secured === 'true',
                element.dataset.connected === 'true'
            );
            break;
        case 'connect-wifi':
            const password = document.getElementById('wifi-password').value;
            connectToWifi(password);
            break;
        case 'browse-exe':
            browseForExe();
            break;
        case 'confirm-add-app':
            confirmAddApp();
            break;
        case 'cancel':
            hideAllDialogs();
            hideContextMenu();
            updateFocusableElements();
            break;
        case 'sleep':
            confirmSleep();
            break;
        case 'shutdown':
            confirmShutdown();
            break;
        case 'restart':
            confirmRestart();
            break;
        case 'exit':
            exitLauncher();
            break;
        case 'check-update':
            checkForUpdates();
            break;
        case 'download-update':
            downloadUpdate();
            break;
        case 'cancel-update':
            cancelUpdateDownload();
            break;
        case 'confirm-yes':
            handleConfirmYes();
            break;
        case 'confirm-no':
            hideAllDialogs();
            updateFocusableElements();
            break;
        case 'remove-app':
            hideContextMenu();
            if (state.selectedAppName) {
                showRemoveAppConfirm(state.selectedAppName);
            }
            break;
        case 'controller-settings':
            showControllerSettings();
            break;
        case 'refresh-config':
            refreshConfig();
            break;
        case 'map-button':
            startButtonMapping(element.dataset.button);
            break;
        case 'cancel-mapping':
            cancelControllerMapping();
            break;
        case 'switch-tab':
            switchTab(element.dataset.tab);
            break;
        case 'show-desktop':
            showDesktop();
            break;
        case 'open-browser':
            openBrowser();
            break;
        case 'start-tailscale':
            startTailscale();
            break;
        case 'open-explorer':
            openExplorer();
            break;
        case 'open-transfer':
            openTransfer();
            break;
        case 'explorer-navigate':
            navigateExplorer(element.dataset.path);
            break;
        case 'explorer-open-file':
            openFile(element.dataset.path);
            break;
        case 'explorer-up':
            explorerUp();
            break;
        case 'explorer-home':
            explorerHome();
            break;
        case 'explorer-drive-change':
            navigateExplorer(element.value);
            break;
        case 'toggle-transfer':
            toggleTransferServer();
            break;
        case 'change-transfer-path':
            changeTransferPath();
            break;
        // IPTV actions
        case 'add-iptv-playlist':
            showAddIptvPlaylistDialog();
            break;
        case 'confirm-add-iptv':
            confirmAddIptvPlaylist();
            break;
        case 'cancel-add-iptv':
            hideAllDialogs();
            updateFocusableElements();
            break;
        case 'open-iptv-playlist':
            openIptvPlaylist(element.dataset.playlistId);
            break;
        case 'refresh-iptv-playlist':
            refreshIptvPlaylist(element.dataset.playlistId);
            break;
        case 'remove-iptv-playlist':
            removeIptvPlaylist(element.dataset.playlistId);
            break;
        case 'play-iptv-channel':
            playIptvChannel(element.dataset.channelId);
            break;
        case 'iptv-back-to-playlists':
            iptvBackToPlaylists();
            break;
        case 'iptv-back-to-channels':
            iptvBackToChannels();
            break;
        case 'close-iptv-error':
            closeIptvErrorDialog();
            break;
    }
}

async function launchApp(appName) {
    if (bridge) {
        const resultJson = bridge.LaunchApp(appName);
        const result = JSON.parse(resultJson);
        if (!result.success) {
            showConfirmDialog('Launch Failed', result.error || 'Failed to launch application', null);
        }
    }
}

// ============================
// Input Handling
// ============================

// Mouse click handlers for all interactive elements
function setupMouseClickHandlers() {
    // Use event delegation for better performance
    document.body.addEventListener('click', handleMouseClick);

    // Handle right-click context menu on app tiles
    document.body.addEventListener('contextmenu', handleContextMenuClick);
}

function handleMouseClick(e) {
    const target = e.target.closest('[data-action]');
    if (!target) return;

    const action = target.dataset.action;

    // Update focus to clicked element
    const focusIndex = state.focusableElements.indexOf(target);
    if (focusIndex !== -1) {
        focusElement(focusIndex);
    }

    // Handle actions
    switch (action) {
        case 'launch-app':
            launchApp(target.dataset.appName);
            break;
        case 'add-app':
            showAddAppDialog();
            break;
        case 'wifi':
            showWifiScreen();
            break;
        case 'back':
            goBack();
            break;
        case 'refresh-wifi':
            loadWifiNetworks();
            break;
        case 'select-wifi':
            selectWifi(
                target.dataset.ssid,
                target.dataset.secured === 'true',
                target.dataset.connected === 'true'
            );
            break;
        case 'connect-wifi':
            const password = document.getElementById('wifi-password').value;
            connectToWifi(password);
            break;
        case 'browse-exe':
            browseForExe();
            break;
        case 'confirm-add-app':
            confirmAddApp();
            break;
        case 'cancel':
            hideAllDialogs();
            hideContextMenu();
            updateFocusableElements();
            break;
        case 'sleep':
            confirmSleep();
            break;
        case 'shutdown':
            confirmShutdown();
            break;
        case 'restart':
            confirmRestart();
            break;
        case 'exit':
            exitLauncher();
            break;
        case 'check-update':
            checkForUpdates();
            break;
        case 'download-update':
            downloadUpdate();
            break;
        case 'cancel-update':
            cancelUpdateDownload();
            break;
        case 'confirm-yes':
            handleConfirmYes();
            break;
        case 'confirm-no':
            hideAllDialogs();
            updateFocusableElements();
            break;
        case 'remove-app':
            hideContextMenu();
            if (state.selectedAppName) {
                showRemoveAppConfirm(state.selectedAppName);
            }
            break;
        case 'controller-settings':
            showControllerSettings();
            break;
        case 'refresh-config':
            refreshConfig();
            break;
        case 'map-button':
            startButtonMapping(target.dataset.button);
            break;
        case 'cancel-mapping':
            cancelControllerMapping();
            break;
        case 'brightness-up':
            adjustBrightness(10);
            break;
        case 'brightness-down':
            adjustBrightness(-10);
            break;
        case 'switch-tab':
            switchTab(target.dataset.tab);
            break;
        case 'show-desktop':
            showDesktop();
            break;
        case 'open-browser':
            openBrowser();
            break;
        case 'start-tailscale':
            startTailscale();
            break;
        case 'open-explorer':
            openExplorer();
            break;
        case 'open-transfer':
            openTransfer();
            break;
        case 'explorer-navigate':
            navigateExplorer(target.dataset.path);
            break;
        case 'explorer-open-file':
            openFile(target.dataset.path);
            break;
        case 'explorer-up':
            explorerUp();
            break;
        case 'explorer-home':
            explorerHome();
            break;
        case 'explorer-drive-change':
            navigateExplorer(target.value);
            break;
        case 'toggle-transfer':
            toggleTransferServer();
            break;
        case 'change-transfer-path':
            changeTransferPath();
            break;
        // IPTV actions
        case 'add-iptv-playlist':
            showAddIptvPlaylistDialog();
            break;
        case 'confirm-add-iptv':
            confirmAddIptvPlaylist();
            break;
        case 'cancel-add-iptv':
            hideAllDialogs();
            updateFocusableElements();
            break;
        case 'open-iptv-playlist':
            openIptvPlaylist(target.dataset.playlistId);
            break;
        case 'refresh-iptv-playlist':
            refreshIptvPlaylist(target.dataset.playlistId);
            break;
        case 'remove-iptv-playlist':
            removeIptvPlaylist(target.dataset.playlistId);
            break;
        case 'play-iptv-channel':
            playIptvChannel(target.dataset.channelId);
            break;
        case 'iptv-back-to-playlists':
            iptvBackToPlaylists();
            break;
        case 'iptv-back-to-channels':
            iptvBackToChannels();
            break;
        case 'close-iptv-error':
            closeIptvErrorDialog();
            break;
    }
}

function handleContextMenuClick(e) {
    const appTile = e.target.closest('[data-action="launch-app"]');
    if (appTile) {
        e.preventDefault();
        showContextMenu(appTile.dataset.appName, e.clientX, e.clientY);
    }
}

// ============================
// Cursor Auto-Hide
// ============================
const CURSOR_HIDE_DELAY = 3000; // 3 seconds

function setupCursorAutoHide() {
    // Show cursor on mouse movement
    document.addEventListener('mousemove', showCursor);
    document.addEventListener('mousedown', showCursor);

    // Start with cursor hidden (TV mode)
    hideCursor();
}

function showCursor() {
    if (!state.isCursorVisible) {
        document.body.classList.remove('cursor-hidden');
        state.isCursorVisible = true;
    }

    // Reset the hide timer
    if (state.cursorHideTimeout) {
        clearTimeout(state.cursorHideTimeout);
    }

    // Hide cursor after 3 seconds of inactivity
    state.cursorHideTimeout = setTimeout(hideCursor, CURSOR_HIDE_DELAY);
}

function hideCursor() {
    document.body.classList.add('cursor-hidden');
    state.isCursorVisible = false;
}

function setupGamepadListener() {
    // Listen for messages from C# (gamepad and keyboard input)
    // C# sends both gamepad and keyboard as unified 'gamepad' messages via PostWebMessageAsString
    if (window.chrome?.webview) {
        window.chrome.webview.addEventListener('message', (event) => {
            let data;
            try {
                data = typeof event.data === 'string' ? JSON.parse(event.data) : event.data;
            } catch {
                return;
            }

            if (data.type === 'gamepad') {
                handleGamepadInput(data);
            } else if (data.type === 'controllerMapping') {
                handleControllerMappingInput(data);
            } else if (data.type === 'transferLog') {
                addTransferLogEntry(data.time, data.message, data.isError);
            } else if (data.type === 'transferStatus') {
                state.transferServerRunning = data.isRunning;
                updateTransferUI(data.isRunning);
            } else if (data.type === 'updateProgress') {
                updateProgressUI(data.phase, data.progress, data.message);
            } else if (data.type === 'updateError') {
                showUpdateError(data.error);
            }
        });
    } else {
        // Fallback for development/testing without C# backend - handle keyboard directly
        document.addEventListener('keydown', handleKeyboardInput);
    }
}

function handleKeyboardInput(e) {
    // Don't handle keyboard when typing in input fields
    if (e.target.tagName === 'INPUT') {
        if (e.key === 'Escape') {
            e.target.blur();
            e.preventDefault();
        }
        return;
    }

    // Handle Escape to cancel mapping dialog
    if (state.isMappingMode && e.key === 'Escape') {
        e.preventDefault();
        cancelControllerMapping();
        return;
    }

    switch (e.key) {
        case 'ArrowUp':
            e.preventDefault();
            navigateFocus('up');
            break;
        case 'ArrowDown':
            e.preventDefault();
            navigateFocus('down');
            break;
        case 'ArrowLeft':
            e.preventDefault();
            navigateFocus('left');
            break;
        case 'ArrowRight':
            e.preventDefault();
            navigateFocus('right');
            break;
        case 'Enter':
            e.preventDefault();
            activateFocused();
            break;
        case 'Escape':
        case 'Backspace':
            e.preventDefault();
            goBack();
            break;
        case 'Delete':
            e.preventDefault();
            // Show context menu for focused app tile
            const element = state.focusableElements[state.focusedIndex];
            if (element?.dataset.action === 'launch-app') {
                const rect = element.getBoundingClientRect();
                showContextMenu(element.dataset.appName, rect.right, rect.top);
            }
            break;
    }
}

function handleGamepadInput(data) {
    // When in mapping mode, ignore all gamepad input (raw input is handled separately)
    if (state.isMappingMode) {
        return;
    }

    // When in controller settings screen, only allow navigation and basic actions
    const inControllerSettings = state.currentScreen === 'controller-screen';

    if (data.action === 'direction') {
        switch (data.direction) {
            case 'Up':
                navigateFocus('up');
                break;
            case 'Down':
                navigateFocus('down');
                break;
            case 'Left':
                navigateFocus('left');
                break;
            case 'Right':
                navigateFocus('right');
                break;
        }
    } else if (data.action === 'button') {
        // In controller settings, only allow A (select) and B/Back (back) buttons
        if (inControllerSettings && !['A', 'B', 'Back'].includes(data.button)) {
            return;
        }

        switch (data.button) {
            case 'A':
                activateFocused();
                break;
            case 'B':
            case 'Back':
                goBack();
                break;
            case 'Y':
                // Show add dialog based on current tab
                if (state.currentTab === 'iptv' && state.iptvView === 'playlists') {
                    showAddIptvPlaylistDialog();
                } else if (state.currentTab === 'apps') {
                    showAddAppDialog();
                }
                break;
            case 'X':
                // Show context menu for focused app tile
                const element = state.focusableElements[state.focusedIndex];
                if (element?.dataset.action === 'launch-app') {
                    const rect = element.getBoundingClientRect();
                    showContextMenu(element.dataset.appName, rect.right, rect.top);
                }
                break;
            case 'Start':
                if (state.currentScreen === 'main-screen') {
                    // Switch to settings tab
                    switchTab('settings');
                } else {
                    goBack();
                }
                break;
            case 'LB':
            case 'LeftBumper':
                // Tab switching on main screen
                if (state.currentScreen === 'main-screen') {
                    prevTab();
                }
                break;
            case 'RB':
            case 'RightBumper':
                // Tab switching on main screen
                if (state.currentScreen === 'main-screen') {
                    nextTab();
                }
                break;
        }
    }
}

// ============================
// Utilities
// ============================
function updateClock() {
    const now = new Date();

    // Update header time
    const headerTime = document.getElementById('header-time');
    if (headerTime) {
        headerTime.textContent = now.toLocaleTimeString([], { hour: '2-digit', minute: '2-digit' });
    }

    // Update header date
    const headerDate = document.getElementById('header-date');
    if (headerDate) {
        headerDate.textContent = now.toLocaleDateString([], { weekday: 'short', month: 'short', day: 'numeric' });
    }
}

function escapeHtml(text) {
    if (!text) return '';
    const div = document.createElement('div');
    div.textContent = text;
    return div.innerHTML;
}

// ============================
// Controller Settings
// ============================
function showControllerSettings() {
    showScreen('controller-screen');
    loadControllerConfig();
    updateControllerStatus();
}

function loadControllerConfig() {
    try {
        if (bridge) {
            const configJson = bridge.GetControllerConfig();
            state.controllerConfig = JSON.parse(configJson);
            updateControllerDisplay();
        } else {
            // Mock data for development
            state.controllerConfig = {
                buttonA: 2, buttonB: 4, buttonX: 1, buttonY: 8,
                buttonLB: 16, buttonRB: 32, buttonBack: 64, buttonStart: 128
            };
            updateControllerDisplay();
        }
    } catch (error) {
        console.error('Failed to load controller config:', error);
    }
}

function updateControllerStatus() {
    const statusEl = document.getElementById('controller-status');
    let connected = false;

    if (bridge) {
        try {
            connected = bridge.IsGamepadConnected();
        } catch { }
    }

    if (connected) {
        statusEl.innerHTML = `
            <span class="status-icon connected">&#127918;</span>
            <span class="status-text">Controller connected</span>
        `;
    } else {
        statusEl.innerHTML = `
            <span class="status-icon disconnected">&#127918;</span>
            <span class="status-text">No controller detected</span>
        `;
    }
}

function updateControllerDisplay() {
    if (!state.controllerConfig) return;

    // Update each button display
    const buttonMap = {
        'a': 'buttonA',
        'b': 'buttonB',
        'x': 'buttonX',
        'y': 'buttonY',
        'lb': 'buttonLB',
        'rb': 'buttonRB',
        'back': 'buttonBack',
        'start': 'buttonStart'
    };

    for (const [btnId, configKey] of Object.entries(buttonMap)) {
        const valueEl = document.getElementById(`btn-${btnId}-value`);
        if (valueEl) {
            const rawValue = state.controllerConfig[configKey];
            valueEl.textContent = getButtonName(rawValue);
        }
    }
}

function getButtonName(rawValue) {
    // Convert raw value to button name (B1, B2, etc.)
    for (let i = 0; i < 16; i++) {
        if (rawValue === (1 << i)) {
            return `B${i + 1}`;
        }
    }
    return `0x${rawValue.toString(16).toUpperCase()}`;
}

function startButtonMapping(buttonName) {
    state.mappingButton = buttonName;
    state.isMappingMode = true;

    // Update dialog text
    const buttonLabels = {
        'a': 'A (Select)',
        'b': 'B (Back)',
        'x': 'X (Context)',
        'y': 'Y (Add App)',
        'lb': 'LB (Left Bumper)',
        'rb': 'RB (Right Bumper)',
        'back': 'Back/Select',
        'start': 'Start (Menu)'
    };

    document.getElementById('mapping-button-label').textContent = `Mapping: ${buttonLabels[buttonName] || buttonName}`;
    document.getElementById('mapping-detected').textContent = 'Waiting for input...';
    document.getElementById('mapping-dialog').classList.remove('hidden');

    // Start mapping mode in C#
    if (bridge) {
        bridge.StartControllerMapping();
    }

    updateFocusableElements();
}

function cancelControllerMapping() {
    state.mappingButton = null;
    state.isMappingMode = false;
    document.getElementById('mapping-dialog').classList.add('hidden');

    // Stop mapping mode in C#
    if (bridge) {
        bridge.StopControllerMapping();
    }

    updateFocusableElements();
}

function handleControllerMappingInput(data) {
    if (!state.isMappingMode || !state.mappingButton) return;

    const rawButton = data.rawButton;
    const buttonName = data.buttonName;

    // Update display
    document.getElementById('mapping-detected').textContent = `Detected: ${buttonName}`;

    // Save the mapping
    if (bridge) {
        bridge.SetControllerButton(state.mappingButton, rawButton);
    }

    // Update local config
    const configKeyMap = {
        'a': 'buttonA',
        'b': 'buttonB',
        'x': 'buttonX',
        'y': 'buttonY',
        'lb': 'buttonLB',
        'rb': 'buttonRB',
        'back': 'buttonBack',
        'start': 'buttonStart'
    };

    if (state.controllerConfig && configKeyMap[state.mappingButton]) {
        state.controllerConfig[configKeyMap[state.mappingButton]] = rawButton;
    }

    // Update UI and close dialog after a short delay
    setTimeout(() => {
        cancelControllerMapping();
        updateControllerDisplay();
    }, 500);
}

// ============================
// Tab Navigation
// ============================
state.currentTab = 'apps';

function switchTab(tabName) {
    // Update tab buttons
    document.querySelectorAll('.tab-btn').forEach(btn => {
        btn.classList.toggle('active', btn.dataset.tab === tabName);
    });

    // Update tab content
    document.querySelectorAll('.tab-content').forEach(content => {
        content.classList.toggle('active', content.id === `tab-${tabName}`);
    });

    state.currentTab = tabName;

    // Load data for specific tabs
    if (tabName === 'settings') {
        loadBrightness();
        updateWifiStatus();
        loadVersion();
    } else if (tabName === 'iptv') {
        loadIptvPlaylists();
    }

    // Update focusable elements for new tab and focus first item
    updateFocusableElements();
    state.focusedIndex = 0;
    focusElement(0);
}

function nextTab() {
    const tabs = ['apps', 'iptv', 'utilities', 'settings'];
    const currentIndex = tabs.indexOf(state.currentTab);
    const nextIndex = (currentIndex + 1) % tabs.length;
    switchTab(tabs[nextIndex]);
}

function prevTab() {
    const tabs = ['apps', 'iptv', 'utilities', 'settings'];
    const currentIndex = tabs.indexOf(state.currentTab);
    const prevIndex = (currentIndex - 1 + tabs.length) % tabs.length;
    switchTab(tabs[prevIndex]);
}

// ============================
// Desktop & Browser Utilities
// ============================

function showDesktop() {
    if (bridge) {
        bridge.ShowDesktop();
    }
}

function openBrowser() {
    if (bridge) {
        bridge.OpenBrowser();
    }
}

function startTailscale() {
    if (bridge) {
        const result = JSON.parse(bridge.StartTailscale());
        showToast(result.message);
    }
}

// ============================
// File Explorer
// ============================
state.explorerPath = '';
state.explorerHistory = [];

function openExplorer() {
    showScreen('explorer-screen');
    loadDriveSelector();
    loadDrives();
}

function loadDriveSelector() {
    const selectEl = document.getElementById('explorer-drive-select');
    if (!selectEl || !bridge) return;

    try {
        const json = bridge.GetDrives();
        const drives = JSON.parse(json);

        selectEl.innerHTML = '';
        drives.forEach(drive => {
            const option = document.createElement('option');
            option.value = drive.name;
            option.textContent = `${drive.label} (${drive.name.replace('\\', '')})`;
            selectEl.appendChild(option);
        });

        // Add change event listener
        selectEl.onchange = () => {
            navigateExplorer(selectEl.value);
        };
    } catch (error) {
        console.error('Failed to load drive selector:', error);
    }
}

function updateDriveSelector(currentPath) {
    const selectEl = document.getElementById('explorer-drive-select');
    if (!selectEl) return;

    // Extract drive letter from current path (e.g., "C:\" from "C:\Users\...")
    const driveLetter = currentPath.substring(0, 3);

    // Find and select the matching option
    Array.from(selectEl.options).forEach(option => {
        if (option.value === driveLetter) {
            option.selected = true;
        }
    });
}

async function loadDrives() {
    const listEl = document.getElementById('explorer-list');
    const pathEl = document.getElementById('explorer-path');

    pathEl.textContent = 'This PC';
    state.explorerPath = '';
    state.explorerHistory = [];

    listEl.innerHTML = '<div class="loading"><div class="spinner"></div></div>';

    try {
        let drives = [];
        if (bridge) {
            const json = bridge.GetDrives();
            drives = JSON.parse(json);
        }

        listEl.innerHTML = '';

        drives.forEach(drive => {
            const item = document.createElement('div');
            item.className = 'explorer-item drive focusable';
            item.dataset.action = 'explorer-navigate';
            item.dataset.path = drive.name;
            item.tabIndex = 0;

            const usedSpace = drive.totalSize - drive.freeSpace;
            const usedPercent = ((usedSpace / drive.totalSize) * 100).toFixed(0);

            item.innerHTML = `
                <span class="item-icon">&#128191;</span>
                <div class="item-info">
                    <div class="item-name">${escapeHtml(drive.label)} (${escapeHtml(drive.name.replace('\\', ''))})</div>
                    <div class="item-meta">${formatSize(drive.freeSpace)} free of ${formatSize(drive.totalSize)} (${usedPercent}% used)</div>
                </div>
            `;

            listEl.appendChild(item);
        });

        updateFocusableElements();
        focusElement(0);
    } catch (error) {
        console.error('Failed to load drives:', error);
        listEl.innerHTML = '<div class="empty-state"><span class="empty-icon">&#128543;</span><p>Failed to load drives</p></div>';
    }
}

async function navigateExplorer(path) {
    const listEl = document.getElementById('explorer-list');
    const pathEl = document.getElementById('explorer-path');

    // Save current path to history
    if (state.explorerPath) {
        state.explorerHistory.push(state.explorerPath);
    }

    state.explorerPath = path;
    pathEl.textContent = path;
    updateDriveSelector(path);

    listEl.innerHTML = '<div class="loading"><div class="spinner"></div></div>';

    try {
        let contents = { items: [], error: null };
        if (bridge) {
            const json = bridge.GetDirectoryContents(path);
            contents = JSON.parse(json);
        }

        if (contents.error) {
            listEl.innerHTML = `<div class="empty-state"><span class="empty-icon">&#128683;</span><p>${escapeHtml(contents.error)}</p></div>`;
            updateFocusableElements();
            return;
        }

        listEl.innerHTML = '';

        contents.items.forEach(item => {
            const el = document.createElement('div');
            el.className = `explorer-item ${item.isDirectory ? 'folder' : 'file'} focusable`;
            el.dataset.action = item.isDirectory ? 'explorer-navigate' : 'explorer-open-file';
            el.dataset.path = item.path;
            el.tabIndex = 0;

            const icon = item.isDirectory ? '&#128193;' : getFileIcon(item.extension);
            const meta = item.isDirectory ? 'Folder' : formatSize(item.size);

            el.innerHTML = `
                <span class="item-icon">${icon}</span>
                <div class="item-info">
                    <div class="item-name">${escapeHtml(item.name)}</div>
                    <div class="item-meta">${meta}</div>
                </div>
            `;

            listEl.appendChild(el);
        });

        if (contents.items.length === 0) {
            listEl.innerHTML = '<div class="empty-state"><span class="empty-icon">&#128194;</span><p>Folder is empty</p></div>';
        }

        updateFocusableElements();
        focusElement(0);
    } catch (error) {
        console.error('Failed to load directory:', error);
        listEl.innerHTML = '<div class="empty-state"><span class="empty-icon">&#128543;</span><p>Failed to load directory</p></div>';
    }
}

function explorerUp() {
    if (!state.explorerPath) return;

    // Get parent path
    const pathParts = state.explorerPath.replace(/\\/g, '/').split('/').filter(p => p);
    if (pathParts.length <= 1) {
        // At root, go back to drive list
        loadDrives();
    } else {
        // Navigate to parent
        pathParts.pop();
        const parentPath = pathParts.join('\\') + '\\';
        // Don't add to history for up navigation
        state.explorerPath = parentPath;
        document.getElementById('explorer-path').textContent = parentPath;
        navigateExplorerDirect(parentPath);
    }
}

async function navigateExplorerDirect(path) {
    // Same as navigateExplorer but doesn't push to history
    const listEl = document.getElementById('explorer-list');
    const pathEl = document.getElementById('explorer-path');

    state.explorerPath = path;
    pathEl.textContent = path;
    updateDriveSelector(path);

    listEl.innerHTML = '<div class="loading"><div class="spinner"></div></div>';

    try {
        let contents = { items: [], error: null };
        if (bridge) {
            const json = bridge.GetDirectoryContents(path);
            contents = JSON.parse(json);
        }

        if (contents.error) {
            listEl.innerHTML = `<div class="empty-state"><span class="empty-icon">&#128683;</span><p>${escapeHtml(contents.error)}</p></div>`;
            updateFocusableElements();
            return;
        }

        listEl.innerHTML = '';

        contents.items.forEach(item => {
            const el = document.createElement('div');
            el.className = `explorer-item ${item.isDirectory ? 'folder' : 'file'} focusable`;
            el.dataset.action = item.isDirectory ? 'explorer-navigate' : 'explorer-open-file';
            el.dataset.path = item.path;
            el.tabIndex = 0;

            const icon = item.isDirectory ? '&#128193;' : getFileIcon(item.extension);
            const meta = item.isDirectory ? 'Folder' : formatSize(item.size);

            el.innerHTML = `
                <span class="item-icon">${icon}</span>
                <div class="item-info">
                    <div class="item-name">${escapeHtml(item.name)}</div>
                    <div class="item-meta">${meta}</div>
                </div>
            `;

            listEl.appendChild(el);
        });

        if (contents.items.length === 0) {
            listEl.innerHTML = '<div class="empty-state"><span class="empty-icon">&#128194;</span><p>Folder is empty</p></div>';
        }

        updateFocusableElements();
        focusElement(0);
    } catch (error) {
        console.error('Failed to load directory:', error);
        listEl.innerHTML = '<div class="empty-state"><span class="empty-icon">&#128543;</span><p>Failed to load directory</p></div>';
    }
}

function explorerHome() {
    if (bridge) {
        const homePath = bridge.GetUserHome();
        navigateExplorer(homePath);
    }
}

function openFile(path) {
    if (bridge) {
        bridge.OpenFile(path);
    }
}

function getFileIcon(extension) {
    const icons = {
        '.txt': '&#128196;',
        '.pdf': '&#128213;',
        '.doc': '&#128195;', '.docx': '&#128195;',
        '.xls': '&#128200;', '.xlsx': '&#128200;',
        '.ppt': '&#128202;', '.pptx': '&#128202;',
        '.jpg': '&#128247;', '.jpeg': '&#128247;', '.png': '&#128247;', '.gif': '&#128247;', '.bmp': '&#128247;',
        '.mp3': '&#127925;', '.wav': '&#127925;', '.flac': '&#127925;',
        '.mp4': '&#127909;', '.avi': '&#127909;', '.mkv': '&#127909;', '.mov': '&#127909;',
        '.zip': '&#128230;', '.rar': '&#128230;', '.7z': '&#128230;',
        '.exe': '&#128187;',
        '.js': '&#128221;', '.html': '&#128221;', '.css': '&#128221;', '.json': '&#128221;',
    };
    return icons[extension?.toLowerCase()] || '&#128196;';
}

function formatSize(bytes) {
    if (bytes === 0) return '0 B';
    const sizes = ['B', 'KB', 'MB', 'GB', 'TB'];
    const i = Math.floor(Math.log(bytes) / Math.log(1024));
    return (bytes / Math.pow(1024, i)).toFixed(i > 0 ? 1 : 0) + ' ' + sizes[i];
}

// ============================
// Simple Transfer
// ============================
state.transferServerRunning = false;

function openTransfer() {
    showScreen('transfer-screen');
    loadTransferStatus();
}

function loadTransferStatus() {
    if (!bridge) return;

    try {
        state.transferServerRunning = bridge.IsTransferServerRunning();
        const savePath = bridge.GetTransferSavePath();

        document.getElementById('transfer-save-path').textContent = savePath;
        updateTransferUI(state.transferServerRunning);
    } catch (error) {
        console.error('Failed to load transfer status:', error);
    }
}

function updateTransferUI(isRunning) {
    const statusEl = document.getElementById('transfer-status');
    const toggleBtn = document.getElementById('transfer-toggle-btn');
    const urlBox = document.getElementById('transfer-url-box');

    if (isRunning) {
        statusEl.classList.add('active');
        statusEl.innerHTML = `
            <span class="status-icon">&#128246;</span>
            <span class="status-text">Server Running</span>
        `;
        toggleBtn.textContent = 'Stop Server';
        toggleBtn.classList.remove('primary');
        toggleBtn.classList.add('danger');
        urlBox.classList.remove('hidden');
    } else {
        statusEl.classList.remove('active');
        statusEl.innerHTML = `
            <span class="status-icon">&#128225;</span>
            <span class="status-text">Server Stopped</span>
        `;
        toggleBtn.textContent = 'Start Server';
        toggleBtn.classList.add('primary');
        toggleBtn.classList.remove('danger');
        urlBox.classList.add('hidden');
    }
}

function toggleTransferServer() {
    if (!bridge) return;

    try {
        if (state.transferServerRunning) {
            bridge.StopTransferServer();
            state.transferServerRunning = false;
            updateTransferUI(false);
        } else {
            const resultJson = bridge.StartTransferServer();
            const result = JSON.parse(resultJson);

            if (result.success && result.url) {
                state.transferServerRunning = true;
                document.getElementById('transfer-url').textContent = result.url;
                updateTransferUI(true);
            } else {
                showConfirmDialog('Error', 'Failed to start transfer server. The port may be in use.', null);
            }
        }
    } catch (error) {
        console.error('Failed to toggle transfer server:', error);
    }
}

function changeTransferPath() {
    if (!bridge) return;

    try {
        const newPath = bridge.BrowseForFolder();
        if (newPath) {
            bridge.SetTransferSavePath(newPath);
            document.getElementById('transfer-save-path').textContent = newPath;
        }
    } catch (error) {
        console.error('Failed to change transfer path:', error);
    }
}

function addTransferLogEntry(time, message, isError) {
    const logEl = document.getElementById('transfer-log');
    const entry = document.createElement('div');
    entry.className = `transfer-log-entry ${isError ? 'error' : 'success'}`;
    entry.innerHTML = `
        <span class="log-time">${escapeHtml(time)}</span>
        <span class="log-message">${escapeHtml(message)}</span>
    `;
    logEl.insertBefore(entry, logEl.firstChild);

    // Keep only last 50 entries
    while (logEl.children.length > 50) {
        logEl.removeChild(logEl.lastChild);
    }
}

// ============================
// IPTV Functions
// ============================
state.iptvPlaylists = [];
state.currentPlaylist = null;
state.currentChannel = null;
state.iptvView = 'playlists'; // 'playlists' | 'channels' | 'player'
state.iptvPlayerControlsTimeout = null;

// Initialize IPTV when tab is switched
function loadIptvPlaylists() {
    try {
        if (bridge) {
            const json = bridge.GetIptvPlaylists();
            state.iptvPlaylists = JSON.parse(json);
        } else {
            // Mock data for development
            state.iptvPlaylists = [];
        }
        renderIptvPlaylists();
    } catch (error) {
        console.error('Failed to load IPTV playlists:', error);
        state.iptvPlaylists = [];
        renderIptvPlaylists();
    }
}

function renderIptvPlaylists() {
    const playlistsView = document.getElementById('iptv-playlists-view');
    const channelsView = document.getElementById('iptv-channels-view');
    const playerView = document.getElementById('iptv-player-view');
    const listEl = document.getElementById('iptv-playlist-list');

    // Show playlists view
    playlistsView.style.display = '';
    channelsView.style.display = 'none';
    playerView.style.display = 'none';
    state.iptvView = 'playlists';

    listEl.innerHTML = '';

    if (state.iptvPlaylists.length === 0) {
        // Show empty state
        listEl.innerHTML = `
            <div class="iptv-empty-state">
                <span class="empty-icon">&#128250;</span>
                <h3>No IPTV Playlists</h3>
                <p>Add an M3U/M3U8 playlist link to start watching TV channels</p>
            </div>
        `;
    } else {
        state.iptvPlaylists.forEach(playlist => {
            const item = document.createElement('div');
            item.className = 'iptv-playlist-item focusable';
            item.dataset.action = 'open-iptv-playlist';
            item.dataset.playlistId = playlist.id;
            item.tabIndex = 0;

            const channelCount = playlist.channels ? playlist.channels.length : 0;
            const lastUpdated = playlist.lastUpdated ? new Date(playlist.lastUpdated).toLocaleDateString() : 'Never';

            item.innerHTML = `
                <span class="playlist-icon">&#128250;</span>
                <div class="playlist-info">
                    <div class="playlist-name">${escapeHtml(playlist.name)}</div>
                    <div class="playlist-meta">${channelCount} channels • Updated: ${lastUpdated}</div>
                </div>
                <div class="playlist-actions">
                    <button class="playlist-action-btn focusable" data-action="refresh-iptv-playlist" data-playlist-id="${playlist.id}" tabindex="0">Refresh</button>
                    <button class="playlist-action-btn delete-btn focusable" data-action="remove-iptv-playlist" data-playlist-id="${playlist.id}" tabindex="0">Delete</button>
                </div>
            `;

            listEl.appendChild(item);
        });
    }

    updateFocusableElements();
}

function showAddIptvPlaylistDialog() {
    document.getElementById('iptv-playlist-name').value = '';
    document.getElementById('iptv-playlist-url').value = '';
    document.getElementById('add-iptv-dialog').classList.remove('hidden');
    updateFocusableElements();

    setTimeout(() => {
        document.getElementById('iptv-playlist-name').focus();
    }, 100);
}

// Track IPTV add polling interval
let iptvAddPollInterval = null;

async function confirmAddIptvPlaylist() {
    const name = document.getElementById('iptv-playlist-name').value.trim();
    const url = document.getElementById('iptv-playlist-url').value.trim();

    if (!name) {
        document.getElementById('iptv-playlist-name').focus();
        return;
    }

    if (!url) {
        document.getElementById('iptv-playlist-url').focus();
        return;
    }

    // Validate URL format
    if (!url.match(/^https?:\/\/.+\.(m3u8?|txt)$/i) && !url.match(/^https?:\/\/.+/i)) {
        showIptvError('Please enter a valid M3U/M3U8 URL');
        return;
    }

    try {
        if (bridge) {
            const resultJson = bridge.AddIptvPlaylist(name, url);
            const result = JSON.parse(resultJson);

            if (result.started) {
                // Task started in background, close dialog and notify user
                hideAllDialogs();
                showToast(`Adding "${name}"... You'll be notified when done.`);

                // Start polling for result
                startIptvAddPolling();
            } else if (result.success) {
                // Immediate success (shouldn't happen with new implementation)
                hideAllDialogs();
                loadIptvPlaylists();
                showToast(`Added playlist: ${name}`);
            } else {
                // Immediate error (validation failed)
                showIptvError(result.error || 'Failed to add playlist');
            }
        }
    } catch (error) {
        console.error('Failed to add IPTV playlist:', error);
        showIptvError('Failed to add playlist');
    }
}

function startIptvAddPolling() {
    // Clear any existing polling
    if (iptvAddPollInterval) {
        clearInterval(iptvAddPollInterval);
    }

    // Poll every 500ms for result
    iptvAddPollInterval = setInterval(() => {
        if (bridge) {
            try {
                const resultJson = bridge.CheckIptvAddResult();
                const result = JSON.parse(resultJson);

                if (result.pending) {
                    // Still processing, continue polling
                    return;
                }

                // Stop polling
                clearInterval(iptvAddPollInterval);
                iptvAddPollInterval = null;

                if (result.noResult) {
                    // No result available (shouldn't happen normally)
                    return;
                }

                // Got a result
                if (result.success) {
                    loadIptvPlaylists();
                    showToast(`Playlist "${result.playlist.name}" added with ${result.playlist.channelCount} channels`);
                } else {
                    showToast(`Failed to add playlist: ${result.error}`);
                }
            } catch (error) {
                console.error('Error checking IPTV add result:', error);
                clearInterval(iptvAddPollInterval);
                iptvAddPollInterval = null;
            }
        }
    }, 500);
}

function removeIptvPlaylist(playlistId) {
    const playlist = state.iptvPlaylists.find(p => p.id === playlistId);
    const playlistName = playlist ? playlist.name : 'this playlist';

    showConfirmDialog(
        'Remove Playlist',
        `Are you sure you want to remove "${playlistName}"?`,
        () => {
            if (bridge) {
                const resultJson = bridge.RemoveIptvPlaylist(playlistId);
                const result = JSON.parse(resultJson);

                if (result.success) {
                    loadIptvPlaylists();
                    showToast('Playlist removed');
                } else {
                    showIptvError(result.error || 'Failed to remove playlist');
                }
            }
        }
    );
}

// Track IPTV refresh polling interval
let iptvRefreshPollInterval = null;
let pendingRefreshPlaylistId = null;
let pendingRefreshOpenAfter = false;

async function refreshIptvPlaylist(playlistId, openAfterRefresh = false) {
    try {
        if (bridge) {
            const resultJson = bridge.RefreshIptvPlaylist(playlistId);
            const result = JSON.parse(resultJson);

            if (result.started) {
                // Task started in background, notify user
                showToast(`Refreshing "${result.name}"...`);
                pendingRefreshPlaylistId = playlistId;
                pendingRefreshOpenAfter = openAfterRefresh;

                // Start polling for result
                if (iptvRefreshPollInterval) {
                    clearInterval(iptvRefreshPollInterval);
                }

                iptvRefreshPollInterval = setInterval(() => {
                    const checkJson = bridge.CheckIptvRefreshResult();
                    const checkResult = JSON.parse(checkJson);

                    if (!checkResult.pending) {
                        clearInterval(iptvRefreshPollInterval);
                        iptvRefreshPollInterval = null;

                        if (checkResult.noResult) {
                            return;
                        }

                        if (checkResult.success) {
                            loadIptvPlaylists();
                            showToast(`"${checkResult.name}" updated: ${checkResult.channelCount} channels`);

                            // If requested, open the playlist after refresh
                            if (pendingRefreshOpenAfter && pendingRefreshPlaylistId) {
                                openIptvPlaylist(pendingRefreshPlaylistId);
                            }
                        } else {
                            showToast(`Failed to refresh "${checkResult.name}": ${checkResult.error}`);
                        }

                        pendingRefreshPlaylistId = null;
                        pendingRefreshOpenAfter = false;
                    }
                }, 500);
            } else if (result.error) {
                showToast(result.error);
            }
        }
    } catch (error) {
        console.error('Failed to refresh IPTV playlist:', error);
        showToast('Failed to refresh playlist');
    }
}

function openIptvPlaylist(playlistId) {
    const playlist = state.iptvPlaylists.find(p => p.id === playlistId);
    if (!playlist) return;

    state.currentPlaylist = playlist;

    // Fetch channels from backend
    if (bridge) {
        try {
            const channelsJson = bridge.GetPlaylistChannels(playlistId);
            const channels = JSON.parse(channelsJson);

            if (channels.error) {
                showIptvError(channels.error);
                return;
            }

            // Store channels in current playlist
            state.currentPlaylist.channels = channels;

            if (!channels || channels.length === 0) {
                showIptvError('No channels found in this playlist');
                return;
            }

            renderIptvChannels(state.currentPlaylist);
        } catch (error) {
            console.error('Failed to load channels:', error);
            showIptvError('Failed to load channels');
        }
    }
}

function renderIptvChannels(playlist) {
    const playlistsView = document.getElementById('iptv-playlists-view');
    const channelsView = document.getElementById('iptv-channels-view');
    const playerView = document.getElementById('iptv-player-view');
    const titleEl = document.getElementById('iptv-playlist-title');
    const gridEl = document.getElementById('iptv-channel-grid');

    // Show channels view
    playlistsView.style.display = 'none';
    channelsView.style.display = '';
    playerView.style.display = 'none';
    state.iptvView = 'channels';

    titleEl.textContent = playlist.name;
    gridEl.innerHTML = '';

    if (!playlist.channels || playlist.channels.length === 0) {
        gridEl.innerHTML = `
            <div class="iptv-empty-state">
                <span class="empty-icon">&#128250;</span>
                <h3>No Channels</h3>
                <p>This playlist doesn't have any channels</p>
            </div>
        `;
        updateFocusableElements();
        return;
    }

    playlist.channels.forEach(channel => {
        const tile = document.createElement('div');
        const isFailed = channel.failed === true;
        tile.className = `iptv-channel-tile focusable${isFailed ? ' channel-failed' : ''}`;
        tile.dataset.action = 'play-iptv-channel';
        tile.dataset.channelId = channel.id;
        tile.tabIndex = 0;

        // Channel logo or placeholder
        let logoContent = '';
        if (channel.logo) {
            logoContent = `<div class="channel-logo" style="background-image: url('${escapeHtml(channel.logo)}')"></div>`;
        } else {
            logoContent = `<div class="channel-logo"><span class="placeholder-icon">&#128250;</span></div>`;
        }

        tile.innerHTML = `
            ${logoContent}
            <div class="channel-info">
                <div class="channel-name">${escapeHtml(channel.name)}</div>
                ${channel.group ? `<div class="channel-group">${escapeHtml(channel.group)}</div>` : ''}
                ${isFailed ? '<div class="channel-failed-badge">Not Working</div>' : ''}
            </div>
        `;

        gridEl.appendChild(tile);
    });

    updateFocusableElements();
    focusElement(0);
}

function playIptvChannel(channelId) {
    if (!state.currentPlaylist) return;

    const channel = state.currentPlaylist.channels.find(c => c.id === channelId);
    if (!channel) return;

    state.currentChannel = channel;

    const playlistsView = document.getElementById('iptv-playlists-view');
    const channelsView = document.getElementById('iptv-channels-view');
    const playerView = document.getElementById('iptv-player-view');
    const videoEl = document.getElementById('iptv-video');
    const channelNameEl = document.getElementById('iptv-current-channel');
    const channelGroupEl = document.getElementById('iptv-current-group');
    const channelLogoEl = document.getElementById('iptv-current-logo');

    // Show player view
    playlistsView.style.display = 'none';
    channelsView.style.display = 'none';
    playerView.style.display = '';
    state.iptvView = 'player';

    // Update channel info display
    channelNameEl.textContent = channel.name;
    if (channelGroupEl) {
        channelGroupEl.textContent = channel.group || '';
    }
    if (channelLogoEl) {
        if (channel.logo) {
            channelLogoEl.style.backgroundImage = `url('${channel.logo}')`;
        } else {
            channelLogoEl.style.backgroundImage = '';
        }
    }

    // Set video source
    videoEl.src = channel.url;
    videoEl.load();
    videoEl.play().catch(error => {
        console.error('Failed to play stream:', error);
        // Mark channel as failed and go back to channels
        markChannelAsFailed(state.currentPlaylist?.id, channel.id);
        iptvBackToChannels();
        showToast(`Failed to play "${channel.name}"`);
    });

    // Setup video event handlers
    setupVideoEventHandlers();

    // Show controls initially, then auto-hide
    showIptvPlayerControls();
    startIptvControlsAutoHide();

    updateFocusableElements();
}

function setupVideoEventHandlers() {
    const videoEl = document.getElementById('iptv-video');

    // Remove existing handlers to avoid duplicates
    videoEl.onerror = null;
    videoEl.onended = null;

    videoEl.onerror = function(e) {
        console.error('Video error:', e);
        const channelName = state.currentChannel ? state.currentChannel.name : 'channel';
        // Mark channel as failed and go back to channels
        if (state.currentPlaylist && state.currentChannel) {
            markChannelAsFailed(state.currentPlaylist.id, state.currentChannel.id);
        }
        iptvBackToChannels();
        showToast(`Failed to play "${channelName}"`);
    };

    videoEl.onended = function() {
        // Stream ended - could auto-play next channel or show message
        showToast('Stream ended');
    };
}

function showIptvPlayerControls() {
    const playerView = document.getElementById('iptv-player-view');
    playerView.classList.remove('controls-hidden');
}

function hideIptvPlayerControls() {
    const playerView = document.getElementById('iptv-player-view');
    playerView.classList.add('controls-hidden');
}

function startIptvControlsAutoHide() {
    // Clear existing timeout
    if (state.iptvPlayerControlsTimeout) {
        clearTimeout(state.iptvPlayerControlsTimeout);
    }

    // Hide controls after 5 seconds of inactivity
    state.iptvPlayerControlsTimeout = setTimeout(() => {
        if (state.iptvView === 'player') {
            hideIptvPlayerControls();
        }
    }, 5000);
}

function iptvBackToPlaylists() {
    // Stop video if playing
    const videoEl = document.getElementById('iptv-video');
    videoEl.pause();
    videoEl.src = '';

    state.currentPlaylist = null;
    state.currentChannel = null;
    renderIptvPlaylists();
}

function iptvBackToChannels() {
    // Stop video if playing
    const videoEl = document.getElementById('iptv-video');
    videoEl.pause();
    videoEl.src = '';

    state.currentChannel = null;

    if (state.currentPlaylist) {
        renderIptvChannels(state.currentPlaylist);
    } else {
        iptvBackToPlaylists();
    }
}

function showIptvError(message) {
    document.getElementById('iptv-error-message').textContent = message;
    document.getElementById('iptv-error-dialog').classList.remove('hidden');
    updateFocusableElements();
}

function closeIptvErrorDialog() {
    document.getElementById('iptv-error-dialog').classList.add('hidden');
    updateFocusableElements();
}

function markChannelAsFailed(playlistId, channelId) {
    if (!playlistId || !channelId || !bridge) return;

    try {
        bridge.MarkChannelFailed(playlistId, channelId);
        // Update local state to reflect the failed status
        if (state.currentPlaylist && state.currentPlaylist.id === playlistId) {
            const channel = state.currentPlaylist.channels.find(c => c.id === channelId);
            if (channel) {
                channel.failed = true;
            }
        }
    } catch (error) {
        console.error('Failed to mark channel as failed:', error);
    }
}
