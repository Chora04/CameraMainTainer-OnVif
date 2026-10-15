const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const test = require('node:test');
const vm = require('node:vm');

const razor = fs.readFileSync(path.join(__dirname, '../../src/CameraMaintainer.Web/Pages/Alerts/Index.cshtml'), 'utf8');
const styles = fs.readFileSync(path.join(__dirname, '../../src/CameraMaintainer.Web/wwwroot/css/ops-dense.css'), 'utf8');
const start = razor.indexOf('    let alertDetailsRequest = 0;');
const source = razor.slice(start, razor.indexOf("    document.addEventListener('camera-maintainer:data-refresh'", start));
test('date formatter is public for the Razor card and handoff scripts', () => {
    const app = fs.readFileSync(path.join(__dirname, '../../src/CameraMaintainer.Web/wwwroot/js/app.js'), 'utf8');
    const clock = app.slice(app.indexOf('    function formatTashkentDateTime('), app.indexOf('    window.renderCameraCaseIssues'));
    const runtime = { window: { cameraErrorMessages: {} } };
    vm.runInNewContext(clock, runtime);
    assert.equal(typeof runtime.window.formatTashkentDateTime, 'function');
    assert.match(runtime.window.formatTashkentDateTime('2026-09-22T05:00:00Z'), /10:00:00/);
});
const escapeHtml = value => String(value).replace(/[&<>"']/g, char => ({ '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;', "'": '&#39;' })[char]);
function context(extra = {}) {
    const result = vm.createContext({ escapeHtml, formatTashkentDateTime: value => value, localizeCameraError: value => String(value || ''),
        renderCameraCaseIssues: (issues, options = {}) => `<div data-issues="${issues.length}" data-legacy-report="${escapeHtml(options.legacyReport || '')}"></div>`, ...extra });
    vm.runInContext(source, result);
    return result;
}
function card(overrides = {}) {
    return { alert: { id: 1, title: 'Offline', category: 0, severity: 2, createdAt: '2026-09-22',
        assignedToDisplayName: 'Operator', workReport: 'Power checked', workflowStage: 'working', issues: [{}, {}] },
        cameraName: 'Camera 1', cameraIp: '192.0.2.1', cameraLocation: 'North wing', displayTitle: 'Camera timed out',
        stageTitle: 'Working', canEdit: true, canCreateHandoff: true,
        stages: [{ key: 'working', title: 'Working' }, { key: 'waiting', title: 'Waiting' }], ...overrides };
}

test('full card includes camera, report, every fault, handoffs and audit history', () => {
    const api = context();
    const data = card();
    const html = api.renderFullAlertDetails(data, true);
    const header = api.renderAlertModalHeader(data);
    for (const value of ['Camera 1', '192.0.2.1']) assert.ok(header.identityHtml.includes(value), value);
    assert.equal(header.title, 'Camera timed out');
    assert.ok(header.locationHtml.includes('North wing'));
    assert.ok(header.titleBadgesHtml.includes('Техническая'));
    assert.ok(header.titleBadgesHtml.includes('Working'));
    assert.ok(header.severityHtml.includes('Критическая'));
    for (const value of ['Power checked', 'Operator', 'data-issues="2"',
        'data-alert-handoffs', 'data-alert-history="1"', 'alert-detail-stage-form', 'error-audit-panel" open']) {
        assert.ok(html.includes(value), value);
    }
});

test('full card separates overview and faults without a duplicate report section', () => {
    const html = context().renderFullAlertDetails(card(), true);
    for (const name of ['alert-detail-overview', 'alert-detail-request', 'alert-detail-faults']) {
        assert.ok(html.includes('alert-detail-section ' + name));
    }
    assert.ok(!html.includes('alert-detail-work'));
    assert.equal((html.match(/class="alert-detail-section-heading"/g) || []).length, 3);
    assert.ok(!html.includes('Камера и заявка'));
    for (const repeated of ['Название камеры', 'IP-адрес', 'Название ошибки', 'Локация']) assert.ok(!html.includes(repeated));
    assert.ok(html.includes('data-tone="danger"'));
    assert.ok(html.includes('class="error-audit-chevron"'));
});

test('every full card section spans the complete card width', () => {
    assert.match(styles, /\.alert-detail-overview-grid\s*\{[^}]*grid-template-columns:\s*1fr;/);
    assert.match(styles, /\.alert-detail-columns\s*\{[^}]*grid-template-columns:\s*1fr;/);
});

test('whole issue blocks alternate warning and teal tones', () => {
    assert.match(styles, /\.alert-detail-faults \.alert-issue-list li\s*\{[^}]*--issue-tone:\s*var\(--detail-tone\)/);
    assert.match(styles, /\.alert-detail-faults \.alert-issue-list li:nth-child\(even\)\s*\{[^}]*--issue-tone:\s*var\(--detail-request\)/);
    for (const selector of ['alert-issue-title', 'alert-issue-lifecycle > div', 'alert-issue-diagnosis > div', 'alert-issue-chat > header']) {
        const escaped = selector.replace(/[.*+?^${}()|[\]\\]/g, '\\$&').replace(/\\ /g, '\\s+');
        assert.match(styles, new RegExp(`\\.${escaped}[^}]*var\\(--issue-tone\\)`));
    }
});

test('empty legacy report is not duplicated and informational alerts are not shown as warnings', () => {
    const data = card();
    data.alert.workReport = '';
    data.alert.severity = 0;
    const api = context();
    const html = api.renderFullAlertDetails(data, false);
    const header = api.renderAlertModalHeader(data);
    assert.ok(html.includes('data-legacy-report=""'));
    assert.ok(!html.includes('alert-detail-work'));
    assert.ok(html.includes('data-tone="info"'));
    assert.ok(header.severityHtml.includes('alert-detail-severity info'));
    assert.ok(!header.severityHtml.includes('alert-detail-severity warning'));
});

test('compact full card groups faults and report, with history collapsed by default', () => {
    const html = context().renderFullAlertDetails(card());
    assert.ok(html.includes('class="alert-detail-columns"'));
    assert.ok(!html.includes('error-audit-panel" open'));
    assert.equal((html.match(/<dt>Этап обработки<\/dt>/g) || []).length, 0);
});

test('long descriptions retain disclosure and legacy reports are passed to the issue chat', () => {
    const data = card();
    data.alert.description = 'Detailed diagnosis '.repeat(30) + '<script>bad()</script>';
    data.alert.workReport = 'Work report '.repeat(40);
    const html = context().renderFullAlertDetails(data, false);
    assert.ok(html.includes('data-detail-fold="description"'));
    assert.ok(!html.includes('data-detail-fold="report"'));
    assert.ok(html.includes(escapeHtml(data.alert.description)));
    assert.ok(html.includes(data.alert.workReport));
    assert.ok(!html.includes('<script>'));
});

test('read-only or hidden-column recipient does not get edit, assignment, stage or create-handoff controls', () => {
    const data = card({ canEdit: false, canCreateHandoff: false, canAssignSelf: false, stages: [] });
    const api = context();
    const html = api.renderFullAlertDetails(data, false);
    assert.ok(!html.includes('alert-detail-stage-form'));
    assert.ok(html.includes('data-can-create="false"'));
    assert.ok(!api.renderFullAlertActions(data).includes('editOpenedAlert'));
    assert.ok(!api.renderFullAlertActions(data).includes('takeError'));
});

test('card fields, reports and custom stage labels are escaped', () => {
    const data = card();
    data.alert.title = data.alert.workReport = data.cameraName = data.cameraIp = data.cameraLocation = '<img src=x onerror=alert(1)>';
    data.stages[0].title = '<script>bad()</script>';
    data.stages[0].key = '" onfocus="bad()';
    const api = context();
    const header = api.renderAlertModalHeader(data);
    const html = api.renderFullAlertDetails(data, true) + header.identityHtml + header.locationHtml;
    assert.ok(!html.includes('<img'));
    assert.ok(!html.includes('<script'));
    assert.ok(html.includes('&lt;img'));
    assert.ok(html.includes('&quot; onfocus=&quot;'));
});

test('closed card shows recovery, closure and who closed it', () => {
    const data = card({ canEdit: false, stages: [] });
    Object.assign(data.alert, { autoResolved: true, resolvedAt: '2026-09-23', recoveryDetectedAt: '2026-09-22 15:00', resolvedByDisplayName: 'Manager' });
    const html = context().renderFullAlertDetails(data, true);
    for (const value of ['2026-09-23', '2026-09-22 15:00', 'Manager']) assert.ok(html.includes(value));
});

test('background refresh never requests a closed card or replaces an in-progress stage change', async () => {
    let requests = 0;
    const modal = { hidden: true };
    const details = { dataset: { alertId: '1', saving: 'false' } };
    const api = context({ document: { getElementById: id => id === 'modal-container' ? modal : details },
        apiFetch: () => { requests++; throw new Error('Unexpected request'); } });
    await api.openTransferredAlert(1, true);
    modal.hidden = false;
    details.dataset.saving = 'true';
    await api.openTransferredAlert(1, true);
    details.dataset.saving = 'false';
    details.dataset.loading = 'true';
    await api.openTransferredAlert(1, true);
    assert.equal(requests, 0);
});
