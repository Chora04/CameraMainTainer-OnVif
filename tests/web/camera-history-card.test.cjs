const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const test = require('node:test');
const vm = require('node:vm');

const razor = fs.readFileSync(path.join(__dirname, '../../src/CameraMaintainer.Web/Pages/Cameras/Index.cshtml'), 'utf8');
const source = razor.slice(razor.indexOf('    function renderCameraAlertCard('), razor.indexOf('    function renderCameraAuditEvents('));
function render(alert, allowed = true) {
    const runtime = vm.createContext({ canOpenCameraAlertCard: allowed,
        cameraAlertCategory: () => ({ css: 'technical', text: 'Technical' }),
        cameraAlertStage: () => 'Stage', escapeHtml: String,
        tashkentDateTime: String, renderCameraCaseIssues: () => '', renderCameraAuditEvents: () => '' });
    vm.runInContext(source, runtime);
    return runtime.renderCameraAlertCard({ createdAt: '2026-09-28', ...alert });
}

test('camera history opens the matching full card in active or history view', () => {
    for (const closed of [false, true]) {
        const html = render({ id: 586, alertSerialNumber: '344-1', autoResolved: closed });
        assert.ok(html.includes('/Alerts?view=' + (closed ? 'history' : 'active') + '&amp;alert=586'));
        assert.ok(html.includes('aria-label="Открыть карточку 344-1"'));
        assert.ok(html.includes('data-lucide="chevron-down"'));
        assert.ok(html.includes('data-lucide="maximize-2"'));
        assert.ok(!html.includes('class="camera-alert-card-tools" aria-hidden'));
    }
});

test('full-card shortcut respects section access and rejects invalid identifiers', () => {
    assert.ok(!render({ id: 1 }, false).includes('camera-alert-card-open'));
    for (const id of [0, -1, 1.5, '1" onclick="bad()', NaN]) {
        assert.ok(!render({ id }).includes('camera-alert-card-open'));
    }
});

test('existing Errors deep link opens the full card', () => {
    const alerts = fs.readFileSync(path.join(__dirname, '../../src/CameraMaintainer.Web/Pages/Alerts/Index.cshtml'), 'utf8');
    assert.ok(alerts.includes("new URLSearchParams(window.location.search).get('alert')"));
    assert.ok(alerts.includes('openTransferredAlert(requestedAlertId)'));
});

test('double click opens the record but not its tools; single click keeps its position', () => {
    const runtime = vm.createContext({});
    vm.runInContext(source, runtime);
    let opened = 0, prevented = 0;
    const record = { querySelector: () => ({ click: () => opened++ }) };
    const event = { detail: 1, preventDefault: () => prevented++, target: {
        closest: selector => selector === '.camera-alert-record' ? record : selector === '.camera-alert-card-face' ? {} : null
    } };
    runtime.handleCameraHistoryClick(event);
    assert.equal(prevented, 1);
    assert.equal(opened, 0);
    runtime.openCameraHistoryCard(event);
    assert.equal(opened, 1);
    event.target.closest = () => ({});
    runtime.openCameraHistoryCard(event);
    assert.equal(opened, 1);
    event.target.closest = selector => selector === '.camera-alert-record' ? { querySelector: () => null } : null;
    runtime.openCameraHistoryCard(event);
    assert.equal(opened, 1);
});

test('return restores the selected camera, expanded records, filters and both scroll positions', async () => {
    const storage = new Map();
    const record = { dataset: { alertId: '586' }, open: true };
    const modal = { scrollTop: 230 };
    const location = { origin: 'http://localhost:5100', pathname: '/Cameras', search: '?page=3&search=office' };
    location.href = location.origin + location.pathname + location.search;
    const history = { state: null, replaceState(state) { this.state = state; } };
    let selected, popupOpened = false, restoredScroll;
    const runtime = vm.createContext({ URL, selectedCameraId: 32, canViewCameraAlertHistory: true,
        location, history, window: { scrollY: 410, scrollTo: (_, value) => restoredScroll = value },
        sessionStorage: { setItem: (key, value) => storage.set(key, value), getItem: key => storage.get(key), removeItem: key => storage.delete(key) },
        document: { querySelectorAll: () => [record], getElementById: () => modal },
        selectCamera: async id => { selected = id; }, openCameraAlertHistoryPopup: () => { popupOpened = true; } });
    vm.runInContext(source, runtime);
    const link = { href: '/Alerts?view=history&alert=586' };
    runtime.rememberCameraHistoryReturn({ stopPropagation() {} }, link);
    assert.ok(link.href.includes('fromCameraHistory=1'));
    record.open = false;
    modal.scrollTop = 0;
    assert.equal(await runtime.restoreCameraHistoryReturn(), true);
    assert.equal(selected, 32);
    assert.equal(popupOpened, true);
    assert.equal(record.open, true);
    assert.equal(modal.scrollTop, 230);
    assert.equal(restoredScroll, 410);
    assert.equal(storage.size, 0);
    assert.equal(await runtime.restoreCameraHistoryReturn(), false);
});

test('internal modal replacement does not emit a user-close event', () => {
    const app = fs.readFileSync(path.join(__dirname, '../../src/CameraMaintainer.Web/wwwroot/js/app.js'), 'utf8');
    const closeSource = app.slice(app.indexOf('    window.closeModal ='), app.indexOf('    window.confirmDialog ='));
    let notifications = 0;
    const runtime = vm.createContext({ window: {}, CustomEvent: class {}, document: {
        getElementById: () => ({ hidden: false }), body: { classList: { remove() {} } },
        dispatchEvent: () => notifications++
    } });
    vm.runInContext(closeSource, runtime);
    runtime.window.closeModal({ notify: false });
    assert.equal(notifications, 0);
    runtime.window.closeModal();
    assert.equal(notifications, 1);
});
